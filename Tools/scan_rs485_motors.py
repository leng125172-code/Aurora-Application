#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
RS485电机自动扫描脚本
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
支持设备：
  · 上海瓴控KTECH电机    （私有协议，帧头0x3E）
  · 雷赛智能iCL-RS V2.0  （标准Modbus RTU协议）

运行环境：瑞芯微RK3588 Linux（Debian/Ubuntu）/ Windows（调试用）

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
使用说明：

[Linux - RK3588]
  1. 安装依赖：
       pip3 install pyserial
  2. 配置串口权限（二选一）：
       永久授权：sudo usermod -aG dialout $USER  （重新登录后生效）
       临时授权：sudo chmod 666 /dev/ttyS* /dev/ttyUSB*
  3. 运行脚本：
       python3 motor_scan.py

[Windows - 调试]
  1. 安装依赖：pip install pyserial
  2. 直接运行：python motor_scan.py
     （自动枚举 COM1~COM32）
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
"""

import glob
import sys
import time
import serial
from typing import List, Dict, Callable

# ============================================================
# 协议常量定义
# ============================================================

# ── 瓴控KTECH协议常量（文档1）──────────────────────────────
KTECH_HEADER      = 0x3E          # 帧头固定值
KTECH_CMD         = 0x9A          # 读取电机状态1和错误标志命令码（文档1）
KTECH_ID_MIN      = 1             # 从站ID下限
KTECH_ID_MAX      = 32            # 从站ID上限（0x01~0x20）
KTECH_DEFAULT_BAUD = 115200       # 默认波特率（文档1）
KTECH_ALT_BAUDS   = [9600, 19200, 38400, 57600]   # 备选波特率（文档1）
KTECH_RESP_DATA_LEN = 0x07        # 响应帧数据区固定长度（文档1）
# 响应帧总长度 = 帧头(1)+命令码(1)+ID(1)+数据长度(1)+CMD_SUM(1)+数据(7)+DATA_SUM(1) = 13
KTECH_RESP_LEN    = 13

# ── 雷赛iCL-RS协议常量（文档2 - 标准Modbus RTU）──────────
LEISAI_FUNC_CODE  = 0x03          # 读保持寄存器功能码（文档2）
LEISAI_REG_ADDR   = 0x1003        # 运行状态寄存器地址（文档2）
LEISAI_REG_COUNT  = 0x0001        # 读取寄存器数量：1个（文档2）
LEISAI_ID_MIN     = 1             # 从站ID下限
LEISAI_ID_MAX     = 31            # 从站ID上限（0x01~0x1F，文档2）
LEISAI_DEFAULT_BAUD = 38400       # 默认波特率（文档2）
LEISAI_ALT_BAUDS  = [9600, 19200, 115200]          # 备选波特率（文档2）
# 响应帧总长度 = 从站ID(1)+功能码(1)+字节数(1)+数据(2)+CRC16(2) = 7
LEISAI_RESP_LEN   = 7

# ── 通用扫描参数 ──────────────────────────────────────────
SERIAL_TIMEOUT    = 0.3           # 串口读取超时（秒）
MAX_RETRY         = 2             # 每条探测帧的最大重试次数
INTER_FRAME_DELAY = 0.05          # 帧间延时（秒），避免RS485总线冲突


# ============================================================
# 工具函数：帧构建 & 校验
# ============================================================

def crc16_modbus(data: bytes) -> int:
    """
    计算Modbus CRC16（文档2 - 雷赛iCL-RS协议）
    多项式：0xA001（0x8005的位反转），初始值：0xFFFF
    返回16位CRC整数（低字节在前的小端序含义由调用方处理）
    """
    crc = 0xFFFF
    for byte in data:
        crc ^= byte
        for _ in range(8):
            if crc & 0x0001:
                crc = (crc >> 1) ^ 0xA001
            else:
                crc >>= 1
    return crc


def build_ktech_frame(motor_id: int) -> bytes:
    """
    构建瓴控KTECH探测帧（文档1 - 读取电机状态1和错误标志命令CMD=0x9A）
    帧格式（5字节）：
      Byte0: 0x3E  帧头
      Byte1: 0x9A  命令码
      Byte2: ID    电机从站ID（0x01~0x20）
      Byte3: 0x00  数据长度（无附加数据）
      Byte4: CMD_SUM = (Byte0+Byte1+Byte2+Byte3) & 0xFF  帧命令校验和低8位
    """
    data_len = 0x00
    cmd_sum  = (KTECH_HEADER + KTECH_CMD + motor_id + data_len) & 0xFF
    return bytes([KTECH_HEADER, KTECH_CMD, motor_id, data_len, cmd_sum])


def validate_ktech_response(frame: bytes, expected_id: int) -> bool:
    """
    校验瓴控KTECH响应帧合法性（文档1）
    响应帧格式（13字节）：
      Byte0:    0x3E      帧头
      Byte1:    0x9A      命令码
      Byte2:    ID        与发送ID一致
      Byte3:    0x07      数据长度固定为7字节
      Byte4:    CMD_SUM   = (Byte0+Byte1+Byte2+Byte3) & 0xFF
      Byte5~11: DATA[7]   电机状态数据（7字节）
      Byte12:   DATA_SUM  = sum(Byte5~11) & 0xFF
    """
    if len(frame) < KTECH_RESP_LEN:
        return False
    f = frame[:KTECH_RESP_LEN]

    # 校验帧头
    if f[0] != KTECH_HEADER:
        return False
    # 校验命令码
    if f[1] != KTECH_CMD:
        return False
    # 校验从站ID
    if f[2] != expected_id:
        return False
    # 校验数据长度
    if f[3] != KTECH_RESP_DATA_LEN:
        return False
    # 校验帧命令校验和 CMD_SUM
    if f[4] != (f[0] + f[1] + f[2] + f[3]) & 0xFF:
        return False
    # 校验数据校验和 DATA_SUM
    if f[12] != sum(f[5:12]) & 0xFF:
        return False
    return True


def build_leisai_frame(motor_id: int) -> bytes:
    """
    构建雷赛iCL-RS探测帧（文档2 - 标准Modbus RTU读保持寄存器）
    帧格式（8字节）：
      Byte0:   ID       从站地址（0x01~0x1F）
      Byte1:   0x03     功能码：读保持寄存器
      Byte2:   0x10     寄存器地址高字节（0x1003高字节）
      Byte3:   0x03     寄存器地址低字节（0x1003低字节）
      Byte4:   0x00     读取数量高字节
      Byte5:   0x01     读取数量低字节（读1个寄存器）
      Byte6:   CRC_LOW  CRC16低字节（小端序，低位在前）
      Byte7:   CRC_HIGH CRC16高字节
    """
    payload = bytes([
        motor_id,
        LEISAI_FUNC_CODE,
        (LEISAI_REG_ADDR >> 8) & 0xFF,    # 0x10
        LEISAI_REG_ADDR & 0xFF,            # 0x03
        (LEISAI_REG_COUNT >> 8) & 0xFF,    # 0x00
        LEISAI_REG_COUNT & 0xFF,           # 0x01
    ])
    crc = crc16_modbus(payload)
    return payload + bytes([crc & 0xFF, (crc >> 8) & 0xFF])  # 低字节在前


def validate_leisai_response(frame: bytes, expected_id: int) -> bool:
    """
    校验雷赛iCL-RS响应帧合法性（文档2 - 标准Modbus RTU）
    响应帧格式（7字节）：
      Byte0:   ID    从站地址，与发送ID一致
      Byte1:   0x03  功能码
      Byte2:   0x02  字节数（读1个寄存器 = 2字节）
      Byte3-4: DATA  寄存器值（2字节）
      Byte5-6: CRC16 校验码（低字节在前）
    """
    if len(frame) < LEISAI_RESP_LEN:
        return False
    f = frame[:LEISAI_RESP_LEN]

    # 校验从站ID
    if f[0] != expected_id:
        return False
    # 校验功能码
    if f[1] != LEISAI_FUNC_CODE:
        return False
    # 校验字节数（1个寄存器=2字节）
    if f[2] != 0x02:
        return False
    # 校验CRC16（对前5字节计算，与后2字节比对）
    expected_crc  = crc16_modbus(f[:5])
    received_crc  = f[5] | (f[6] << 8)   # 小端序还原
    if expected_crc != received_crc:
        return False
    return True


# ============================================================
# 串口探测核心函数
# ============================================================

def probe_motor(
    ser: serial.Serial,
    frame: bytes,
    resp_len: int,
    validate_fn: Callable[[bytes, int], bool],
    motor_id: int
) -> bool:
    """
    发送探测帧并校验响应，支持重试机制
    参数：
      ser:         已打开的serial.Serial对象
      frame:       待发送的探测帧
      resp_len:    期望响应帧字节数
      validate_fn: 帧合法性校验函数
      motor_id:    期望响应的从站ID
    返回：True=发现有效电机，False=无响应或校验失败
    """
    for attempt in range(MAX_RETRY):
        try:
            ser.reset_input_buffer()   # 清空接收缓冲区，防止残留数据干扰
            ser.write(frame)
            ser.flush()
            resp = ser.read(resp_len)
            if resp and validate_fn(resp, motor_id):
                return True
            if attempt < MAX_RETRY - 1:
                time.sleep(0.05)       # 重试前短暂等待
        except serial.SerialException as e:
            print(f"\n  [串口异常] {e}")
            return False
    return False


# ============================================================
# 串口枚举
# ============================================================

def enumerate_ports() -> List[str]:
    """
    自动枚举系统可用串口
    Linux：/dev/ttyS*（板载）、/dev/ttyUSB*（USB转串口）
    Windows：COM1~COM32（用于调试）
    """
    if sys.platform.startswith('linux'):
        ports = sorted(glob.glob('/dev/ttyS[0-9]*'))
        ports += sorted(glob.glob('/dev/ttyUSB*'))
        return ports
    elif sys.platform.startswith('win'):
        # Windows：逐一尝试打开 COM 口来枚举
        ports = []
        for i in range(1, 33):
            port = f'COM{i}'
            try:
                s = serial.Serial(port, timeout=0.1)
                s.close()
                ports.append(port)
            except (serial.SerialException, OSError):
                pass
        return ports
    else:
        return []


# ============================================================
# 扫描逻辑
# ============================================================

def get_baudrate_sequence() -> List[int]:
    """
    按扫描优先级生成波特率列表（去重，避免重复扫描）
    优先级：瓴控默认115200 → 雷赛默认38400 → 其余备选（升序）
    """
    ordered   = [KTECH_DEFAULT_BAUD, LEISAI_DEFAULT_BAUD]
    remaining = sorted(
        set(KTECH_ALT_BAUDS + LEISAI_ALT_BAUDS) - set(ordered)
    )
    return ordered + remaining


def scan_port(port: str, results: List[Dict]) -> None:
    """
    对单个串口执行全波特率 × 全ID扫描
    参数：
      port:    串口设备路径
      results: 扫描结果列表（就地追加）
    """
    bauds = get_baudrate_sequence()
    print(f"\n{'━'*62}")
    print(f"  串口: {port}")
    print(f"{'━'*62}")

    for baud in bauds:
        print(f"\n  [波特率 {baud:>7} bps]")
        try:
            with serial.Serial(
                port     = port,
                baudrate = baud,
                bytesize = serial.EIGHTBITS,     # 8数据位（8N1）
                parity   = serial.PARITY_NONE,   # 无校验（8N1）
                stopbits = serial.STOPBITS_ONE,  # 1停止位（8N1）
                timeout  = SERIAL_TIMEOUT
            ) as ser:

                # ── 1. 扫描瓴控KTECH电机（ID 1~32，文档1）────────────
                print(f"    ▶ 瓴控KTECH  ID 1~{KTECH_ID_MAX}", end='', flush=True)
                ktech_hit = 0
                for mid in range(KTECH_ID_MIN, KTECH_ID_MAX + 1):
                    frame = build_ktech_frame(mid)
                    if probe_motor(ser, frame, KTECH_RESP_LEN,
                                   validate_ktech_response, mid):
                        # 首次命中时先换行（结束"▶"进度行），后续命中直接打印
                        prefix = "\n" if ktech_hit == 0 else ""
                        print(f"{prefix}    ✔ 瓴控KTECH  | 串口={port} | 波特率={baud} | ID={mid}")
                        results.append({
                            'port': port, 'brand': '上海瓴控KTECH',
                            'id': mid,   'baudrate': baud
                        })
                        ktech_hit += 1
                    time.sleep(INTER_FRAME_DELAY)
                if ktech_hit == 0:
                    print(" … 未发现")

                # ── 2. 扫描雷赛iCL-RS电机（ID 1~31，文档2）──────────
                print(f"    ▶ 雷赛iCL-RS ID 1~{LEISAI_ID_MAX}", end='', flush=True)
                leisai_hit = 0
                for mid in range(LEISAI_ID_MIN, LEISAI_ID_MAX + 1):
                    frame = build_leisai_frame(mid)
                    if probe_motor(ser, frame, LEISAI_RESP_LEN,
                                   validate_leisai_response, mid):
                        # 首次命中时先换行（结束"▶"进度行），后续命中直接打印
                        prefix = "\n" if leisai_hit == 0 else ""
                        print(f"{prefix}    ✔ 雷赛iCL-RS | 串口={port} | 波特率={baud} | ID={mid}")
                        results.append({
                            'port': port, 'brand': '雷赛智能iCL-RS V2.0',
                            'id': mid,   'baudrate': baud
                        })
                        leisai_hit += 1
                    time.sleep(INTER_FRAME_DELAY)
                if leisai_hit == 0:
                    print(" … 未发现")

        except serial.SerialException as e:
            print(f"  [错误] 无法打开 {port}@{baud}: {e}")
            break   # 串口无法打开则跳过该串口后续全部波特率


def print_results(results: List[Dict]) -> None:
    """输出结构化扫描结果"""
    print(f"\n{'━'*62}")
    print(f"  扫描完成，共发现 {len(results)} 台电机")
    print(f"{'━'*62}")
    if not results:
        print("  未发现任何RS485电机，请排查：")
        print("  · 接线是否正确（RS485 A/B线是否接反）")
        print("  · 电机是否已上电")
        print("  · 串口权限（Linux: sudo usermod -aG dialout $USER）")
        return

    header = f"  {'序号':<4}  {'串口':<14}  {'品牌':<22}  {'ID':<6}  {'波特率'}"
    print(header)
    print(f"  {'-'*58}")
    for idx, m in enumerate(results, 1):
        print(f"  {idx:<4}  {m['port']:<14}  {m['brand']:<22}  "
              f"{m['id']:<6}  {m['baudrate']}")


# ============================================================
# 入口
# ============================================================

def main():
    print("RS485电机自动扫描工具")
    print(f"支持：上海瓴控KTECH / 雷赛智能iCL-RS V2.0")
    print(f"超时={SERIAL_TIMEOUT}s  重试={MAX_RETRY}次  帧间延时={INTER_FRAME_DELAY}s")

    ports = enumerate_ports()
    if not ports:
        print("\n[错误] 未找到任何串口设备")
        print("Linux: 检查 /dev/ttyS* /dev/ttyUSB* 是否存在")
        print("Windows: 检查设备管理器中是否有COM口")
        return

    print(f"\n发现 {len(ports)} 个串口: {', '.join(ports)}")

    results: List[Dict] = []
    for port in ports:
        scan_port(port, results)

    print_results(results)


if __name__ == '__main__':
    main()