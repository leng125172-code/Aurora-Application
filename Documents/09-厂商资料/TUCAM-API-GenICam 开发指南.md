# TUCAM-API-GenICam 开发指南

**厂商**：鑫图光电有限公司
**版权**：(c) 2011-2025 Xintu Photonics Co., Ltd.(TUCESN) 保留所有权利
**官网**：www.tucsen.net
**技术支持**

* 邮箱：<service@tucsen.com>
* 电话：0591-28055080-818
* 传真：0591-28055080-826

## 目录

1. 使用前阅读
2. 简介
3. 概述

   * 3.1 层结构
   * 3.2 原理
   * 3.3 规则
4. 编程指引

   * 4.1 搭建编程环境
   * 4.2 快速上手
5. 参考

   * 5.1 类型和常量
   * 5.2 结构体
   * 5.3 函数
6. 常见问题解答(FAQ)

\---

## 1\. 使用前阅读

本文档与配套示例代码为TUCSEN内部及公开资料，用于协助用户开发适配TUCSEN数字相机的应用程序。

1. 文档与代码仅用于开发用途，**使用风险由用户自行承担**。
2. 文档可能存在技术错误、印刷错误，TUCSEN不承诺更新文档及承担相关损害责任。
3. 所有品牌、产品名称为对应所有者商标/注册商标；**未经TUCSEN书面许可，禁止以任何形式复制、传播、转录、翻译本文档内容**。

\---

## 2\. 简介

### TUCAM-API

TUCAM-API 是控制鑫图光电相机的应用程序接口，支持 **USB、CameraLink、CoaXPress、Gige** 多种数据接口，初始化时自动匹配设备。接口采用标准C语言风格，函数数量精简、易于理解。

### SDK

TUCAM-API 软件开发套件（SDK）包含：头文件、库文件、API函数参考、相机属性文档、示例代码。开发者可基于SDK修改源码、独立开发程序或制作插件。

> 补充说明：部分扩展函数为特定型号相机专属功能，不同相机参数存在差异，参数值仅作参考，详情查阅对应相机性能/属性文档。

\---

## 3\. 概述

### 3.1 层结构

**调用层级**：应用程序 → TUCAM-API → 操作系统 → 驱动 → 相机
TUCSEN 数字相机通过 SDK 对接系统驱动，实现相机控制与图像采集。

### 3.2 原理

1. 相机总线、底层库均由 TUCAM-API 封装，开发者仅需调用 API 层接口。
2. 底层模块可独立迭代，支持新相机、新接口技术，**无需重新编译上层应用**。
3. TUCAM-API 不内置图像显示功能，图像显示由应用程序自行实现，可参考示例代码。

### 3.3 规则

#### 3.3.1 句柄相关

|句柄名称|作用|创建/释放接口|
|-|-|-|
|HDTUCAM|相机设备句柄，绝大多数API必需入参|创建：`TUCAM\_Dev\_Open`<br>释放：`TUCAM\_Dev\_Release`|
|HDTUIMG|图像文件句柄|创建：`TUIMG\_File\_Open`<br>释放：`TUCAM\_File\_Close`|

#### 3.3.2 函数相关

1. **函数命名**
所有API均以 `TUCAM\_` 为前缀，按功能分组：

|函数前缀|功能分类|
|-|-|
|TUCAM\_Api\_|API 初始化/反初始化|
|TUCAM\_Dev\_|相机打开、关闭、信息获取|
|TUCAM\_GenICam\_|GenICam 协议相关接口|
|TUCAM\_Buf\_|内存管理、帧数据操作|
|TUCAM\_File\_|图片/配置文件读写|
|TUCAM\_Rec\_|视频录像保存|
|TUCAM\_Draw\_|Windows 图像绘制接口|

1. **函数参数**
除初始化、打开相机等少数接口外，**绝大多数API第一个参数为 HDTUCAM 相机句柄**；结构体参数标注 `\[in]` 为调用前赋值，`\[out]` 为函数执行后回填数据。
2. **函数返回值**
所有接口返回 `TUCAMRET` 类型结果：

   * 执行成功：`TUCAMRET\_SUCCESS`
   * 开发建议：每次调用API后校验返回值，失败则输出错误信息，便于排错。

#### 3.3.3 属性相关

属性即**相机设备参数**，可通过以下接口实现参数读写、查询：

* `TUCAM\_GenICam\_ElementAttr` / `TUCAM\_GenICam\_ElementAttrNext`：查询属性信息（最值、默认值、类型等）
* `TUCAM\_GenICam\_SetElementValue`：设置属性值
* `TUCAM\_GenICam\_GetElementValue`：读取属性值

每个属性拥有唯一ID、数据类型、取值范围、默认值，详情参考 `TUCAM\_ELEMENT` 结构体。

\---

## 4\. 编程指引

### 4.1 搭建编程环境

#### 4.1.1 VS2013 开发环境配置

1. **新建工程**
新建 **MFC 基于对话框** 项目，.NET Framework 选择 4.5，完成工程创建。
2. **配置头文件目录**
项目属性 → `C/C++` → 常规 → **附加包含目录**，填写 `TUCamApi.h` 所在路径。
3. **配置库文件**

   * 链接器 → 常规 → **附加库目录**，填写 `TUCam.lib` 所在路径。
   * 链接器 → 输入 → **附加依赖项**，填写 `TUCam.lib`。
4. **代码引用**
在源码中引入头文件 `#include "TUCamApi.h"`，区分 x86 / x64 版本库文件。

### 4.2 快速上手

#### 通用调用流程

```Plaintext
初始化API → 打开相机 → 参数配置/采集图像/保存文件 → 关闭相机 → 反初始化API
```

#### 4.2.1 起始和终止（初始化/反初始化）

##### 调用顺序

1. 调用 `TUCAM\_Api\_Init` 初始化SDK，获取在线相机数量；**初始化成功后才可调用其他接口**。
2. 调用 `TUCAM\_Dev\_Open` 打开相机，获取相机句柄。
3. 程序退出前：`TUCAM\_Dev\_Close` 关闭相机 → `TUCAM\_Api\_Uninit` 反初始化SDK。

##### 示例代码

```C
int main (int argc, char\*\* argv)
{
    TUCAM\_INIT itApi;        // SDK初始化参数
    TUCAM\_OPEN opCam;        // 打开相机参数

    itApi.pstrConfigPath = NULL;
    itApi.uiCamCount = 0;

    // 初始化SDK
    if (TUCAMRET\_SUCCESS != TUCAM\_Api\_Init(\&itApi))
    {
        return 0;
    }
    // 无在线相机
    if (0 == itApi.uiCamCount)
    {
        return 0;
    }

    // 打开第0号相机
    opCam.hIdxTUCam = 0;
    opCam.uiIdxOpen = 0;
    if (TUCAMRET\_SUCCESS != TUCAM\_Dev\_Open(\&opCam))
    {
        return 0;
    }

    // 业务逻辑：使用 opCam.hIdxTUCam 句柄操作相机

    // 释放资源
    TUCAM\_Dev\_Close(opCam.hIdxTUCam);
    TUCAM\_Api\_Uninit();
    return 0;
}
```

#### 4.2.2 属性获取和设置

##### 调用顺序

1. 使用 `TUCAM\_GenICam\_ElementAttr` / `TUCAM\_GenICam\_ElementAttrNext` 遍历/查询属性节点（类型、权限、最值）。
2. 使用 `TUCAM\_GenICam\_GetElementValue` / `TUCAM\_GenICam\_SetElementValue` 读写属性值。

> 注意：\*\*数据采集过程中不建议修改属性\*\*，可能返回错误码。

##### 示例代码（属性遍历）

```C
// 访问权限映射
static char s\_access\[]\[4] = {"NI","NA","WO","RO","RW"};
// 数据类型映射
static char s\_elemType\[]\[16] = {
    "Value","Base","Integer","Boolean","Command",
    "Float","String","Register","Category","Enumeration",
    "EnumEntry","Port"
};

char \*pData = NULL;
int nLevel = 0;
TUCAM\_ELEMENT node;
node.pName = "Root";

// 遍历所有属性节点
while (TUCAMRET\_SUCCESS == TUCAM\_GenICam\_ElementAttrNext(opCam.hIdxTUCam, \&node, node.pName))
{
    if (NULL == node.pName) continue;
    switch (node.Type)
    {
        case TU\_ElemCategory:
            nLevel = max(node.Level, 0);
            printf("%\*s\[%d]%s\\r\\n", (nLevel << 1), "", nLevel, node.pName);
            break;
        case TU\_ElemBoolean:
            printf("%\*s\[%d]\[%s]\[%s]%s, %d \\r\\n", (node.Level << 1), "", node.Level,
                   s\_access\[node.Access], s\_elemType\[node.Type], node.pName, node.nVal);
            break;
        // 省略 Integer/Float/String/Enumeration/Command/Register 分支
        default: break;
    }
}
```

#### 4.2.3 内存管理

##### 调用顺序

1. `TUCAM\_Buf\_Alloc`：**采集启动前分配内存**，切换分辨率需重新分配。
2. 启动采集 `TUCAM\_Cap\_Start`，两种取图方式：

   * 方式1：注册回调 `TUCAM\_GenICam\_BuffDataCallBack`，回调中 `TUCAM\_GenICam\_GetBuffData` 获取数据。
   * 方式2：阻塞等待 `TUCAM\_Buf\_WaitForFrame`，配合 `TUCAM\_Buf\_CopyFrame` 拷贝不同格式数据。
3. 停止采集前：`TUCAM\_Buf\_AbortWait` 终止等待。
4. `TUCAM\_Cap\_Stop` 停止采集 → `TUCAM\_Buf\_Release` 释放内存。

##### 帧结构体说明

`TUCAM\_FRAME` 描述单帧图像，`pBuffer` 为数据起始地址；**偏移 `usHeader` 字节后为纯图像数据**。

##### 示例代码（连续采集）

```C
TUCAM\_FRAME m\_frame;
HANDLE m\_hThdGrab;
BOOL m\_bLiving;

// 启动采集
BOOL CDlgTUCam::StartCapture()
{
    m\_frame.pBuffer = NULL;
    m\_frame.ucFormatGet = TUFRM\_FMT\_RGB888;
    m\_frame.uiRsdSize = 1;

    // 分配内存
    if(TUCAMRET\_SUCCESS != TUCAM\_Buf\_Alloc(m\_opCam.hIdxTUCam,\&m\_frame))
        return FALSE;
    // 启动连续采集
    if (TUCAMRET\_SUCCESS != TUCAM\_Cap\_Start(m\_opCam.hIdxTUCam, TUCCM\_SEQUENCE))
    {
        TUCAM\_Buf\_Release(m\_opCam.hIdxTUCam);
        return FALSE;
    }
    m\_bLiving = TRUE;
    m\_hThdGrab = CreateEvent(NULL, TRUE, FALSE, NULL);
    \_beginthread(GrabThread, 0, this);
    return TRUE;
}

// 取图线程
Void \_\_cdecl CDlgTUCam::GrabThread(LPVOID lParam)
{
    CDlgTUCam \*pTuCam = (CDlgTUCam \*)lParam;
    while (pTuCam->m\_bLiving)
    {
        pTuCam->m\_frame.ucFormatGet = TUFRM\_FMT\_RGB888;
        if(TUCAMRET\_SUCCESS == TUCAM\_Buf\_WaitForFrame(pTuCam->m\_opCam.hIdxTUCam, \&pTuCam->m\_frame))
        {
            // 读取帧头信息
            TUCAM\_IMG\_HEADER frmhead;
            memcpy(\&frmhead, pTuCam->m\_frame.pBuffer, sizeof(TUCAM\_IMG\_HEADER));

            // 拷贝其他格式图像
            pTuCam->m\_frame.ucFormatGet = TUFRM\_FMT\_USUAL;
            TUCAM\_Buf\_CopyFrame(pTuCam->m\_opCam.hIdxTUCam, \&pTuCam->m\_frame);
        }
    }
    \_endthread();
    SetEvent(pTuCam->m\_hThdGrab);
}

// 停止采集
void CDlgTUCam::StopCapture()
{
    m\_bLiving = FALSE;
    TUCAM\_Buf\_AbortWait(m\_opCam.hIdxTUCam);
    WaitForSingleObject(m\_hThdGrab, INFINITE);
    CloseHandle(m\_hThdGrab);

    TUCAM\_Cap\_Stop(m\_opCam.hIdxTUCam);
    TUCAM\_Buf\_Release(m\_opCam.hIdxTUCam);
}
```

#### 4.2.4 文件管理

##### 1\. 图片保存流程

```Plaintext
初始化API → 打开相机 → 分配内存 → 启动采集 → 等待帧数据 → TUCAM\_File\_SaveImage 存图 → 终止等待 → 停止采集 → 释放内存 → 反初始化
```

##### 2\. 视频录像流程

录像仅支持 **TUCCM\_SEQUENCE 连续采集模式**

```Plaintext
初始化API → 打开相机 → 分配内存 → 启动采集 → TUCAM\_Rec\_Start 开启录像 → 等待帧数据 → TUCAM\_Rec\_AppendFrame 追加帧 → TUCAM\_Rec\_Stop 结束录像 → 终止等待 → 停止采集 → 释放内存 → 反初始化
```

##### 示例代码

```C
// 保存单张图片
void SaveImage()
{
    m\_frame.ucFormatGet = TUFRM\_FMT\_USUAL;
    if(TUCAMRET\_SUCCESS==TUCAM\_Buf\_WaitForFrame(m\_opCam.hIdxTUCam, \&m\_frame))
    {
        TUCAM\_FILE\_SAVE fileSave;
        fileSave.nSaveFmt = TUFMT\_TIF;          // 保存为TIF格式
        fileSave.pFrame = \&m\_frame;
        fileSave.pstrSavePath = "C:\\\\image";    // 路径+文件名(无后缀)
        TUCAM\_File\_SaveImage(m\_opCam.hIdxTUCam, fileSave);
    }
}

// 开启录像
void StartRecording()
{
    TUCAM\_REC\_SAVE recSave;
    recSave.fFps = 15.0f;
    recSave.nCodec = 0;
    recSave.pstrSavePath = "C:\\\\TUVideo.avi";
    TUCAM\_Rec\_Start(m\_opCam.hIdxTUCam, recSave);
}

// 追加视频帧
void AppendFrame()
{
    m\_frame.ucFormatGet = TUFRM\_FMT\_RGB888;
    if(TUCAMRET\_SUCCESS==TUCAM\_Buf\_WaitForFrame(m\_opCam.hIdxTUCam, \&m\_frame))
    {
        TUCAM\_Rec\_AppendFrame(m\_opCam.hIdxTUCam, \&m\_frame);
    }
}

// 停止录像
void StopRecording()
{
    TUCAM\_Rec\_Stop(m\_opCam.hIdxTUCam);
}
```

#### 4.2.5 其他常用功能

##### 1\. 图像转 OpenCV Mat

```C
if(TUCAMRET\_SUCCESS==TUCAM\_Buf\_WaitForFrame(m\_opCam.hIdxTUCam, \&m\_frame))
{
    int type = CV\_MAKETYPE(m\_frame.ucDepth, m\_frame.ucChannels);
    uchar \*data = m\_frame.pBuffer + m\_frame.usHeader;
    Mat dst = Mat(m\_frame.usHeight, m\_frame.usWidth, type, data);
}
```

##### 2\. 图像转 C# Bitmap

```C#
m\_frame.ucFormatGet = TUFRM\_FMT\_RGB888;
if (TUCamAPI.TUCAM\_Buf\_WaitForFrame(m\_opCam.hIdxTUCam, ref m\_frame) == TUCAMRET\_SUCCESS)
{
    int nSize = (int)(m\_frame.uiImgSize + m\_frame.usHeader);
    byte\[] buffer = new byte\[nSize];
    System.Runtime.InteropServices.Marshal.Copy(m\_frame.pBuffer, buffer, 0, nSize);
    Buffer.BlockCopy(buffer, (int)(m\_frame.usHeader), buffer, 0, (int)(m\_frame.uiImgSize));

    GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
    int scan0 = (int)handle.AddrOfPinnedObject() + (m\_frame.usHeight - 1) \* m\_frame.uiWidthStep;
    Bitmap bitmap = new Bitmap(m\_frame.usWidth, m\_frame.usHeight, -m\_frame.uiWidthStep,
        PixelFormat.Format24bppRgb, (IntPtr)scan0);
    handle.Free();
}
```

##### 3\. 计算图像平均灰度值

支持整图 / ROI 区域，区分 8bit / 16bit 像素：

```C
if(TUCAMRET\_SUCCESS == TUCAM\_Buf\_WaitForFrame(m\_opCam.hIdxTUCam, \&m\_frame))
{
    bool isRoi = false;
    int roiX = 200, roiY = 400, roiW = 800, roiH = 600;
    int startX = isRoi ? roiX : 0;
    int startY = isRoi ? roiY : 0;
    int width = isRoi ? roiW : m\_frame.usWidth;
    int height = isRoi ? roiH : m\_frame.usHeight;
    long long sum = 0;
    int pixels = width \* height;

    if (2 == m\_frame.ucElemBytes) // 16bit 图像
    {
        unsigned short \*data = (unsigned short \*)(m\_frame.pBuffer + m\_frame.usHeader);
        for (int i = startY; i < startY+height; i++)
            for (int j = startX; j < startX+width; j++)
                sum += data\[i \* m\_frame.usWidth + j];
    }
    else // 8bit 图像
    {
        unsigned char \*data = (unsigned char \*)(m\_frame.pBuffer + m\_frame.usHeader);
        for (int i = startY; i < startY+height; i++)
            for (int j = startX; j < startX+width; j++)
                sum += data\[i \* m\_frame.usWidth + j];
    }
    double avg = sum \* 1.0 / pixels; // 平均灰度
}
```

\---

## 5\. 参考

### 5.1 类型和常量

#### 5.1.1 TUCAMRET 错误代码

|名称|代码|描述|
|-|-|-|
|TUCAMRET\_SUCCESS|0x00000001|执行成功|
|TUCAMRET\_RECEIVE\_FINISH|0x00000002|接收帧完成|
|TUCAMRET\_EXTERNAL\_TRIGGER|0x00000003|接收到外部触发|
|TUCAMRET\_FAILURE|0x80000000|接口调用失败|

**初始化错误**

|名称|代码|描述|
|-|-|-|
|TUCAMRET\_NO\_MEMORY|0x80000101|内存不足|
|TUCAMRET\_NO\_RESOURCE|0x80000102|资源不足|
|TUCAMRET\_NO\_MODULE|0x80000103|缺少子模块|
|TUCAMRET\_NO\_DRIVER|0x80000104|缺少驱动|
|TUCAMRET\_NO\_CAMERA|0x80000105|无在线相机|
|TUCAMRET\_FAILOPEN\_CAMERA|0x80000110|打开相机失败|

**状态错误**

|名称|代码|描述|
|-|-|-|
|TUCAMRET\_INIT|0x80000201|API 未初始化|
|TUCAMRET\_BUSY|0x80000202|API 繁忙|
|TUCAMRET\_NOT\_INIT|0x80000203|API 未初始化|
|TUCAMRET\_NOT\_READY|0x80000206|设备未就绪|

**等待错误**

|名称|代码|描述|
|-|-|-|
|TUCAMRET\_ABORT|0x80000207|等待被终止|
|TUCAMRET\_TIMEOUT|0x80000208|超时|
|TUCAMRET\_LOSTFRAME|0x80000209|帧丢失|

**调用错误**

|名称|代码|描述|
|-|-|-|
|TUCAMRET\_INVALID\_HANDLE|0x80000302|无效句柄|
|TUCAMRET\_INVALID\_PARAM|0x80000307|无效参数|
|TUCAMRET\_OUT\_OF\_RANGE|0x80000311|参数越界|
|TUCAMRET\_NOT\_SUPPORT|0x80000312|功能不支持|
|TUCAMRET\_NOT\_WRITABLE|0x80000313|属性只读|
|TUCAMRET\_NOT\_READABLE|0x80000314|属性只写|

**相机/总线错误**

|名称|代码|描述|
|-|-|-|
|TUCAMRET\_FAIL\_READ\_CAMERA|0x83001001|读取相机失败|
|TUCAMRET\_FAIL\_WRITE\_CAMERA|0x83001002|写入相机失败|

#### 5.1.2 TUCAM\_IDINFO 产品信息代码

|名称|代码|描述|
|-|-|-|
|TUIDI\_BUS|0x00|接口类型|
|TUIDI\_VENDOR|0x01|厂商ID|
|TUIDI\_PRODUCT|0x02|产品ID|
|TUIDI\_VERSION\_API|0x03|API版本|
|TUIDI\_VERSION\_FRMW|0x04|固件版本|
|TUIDI\_CAMERA\_MODEL|0x09|相机型号|
|TUIDI\_CURRENT\_WIDTH|0x0A|图像宽度|
|TUIDI\_CURRENT\_HEIGHT|0x0B|图像高度|
|TUIDI\_CAMERA\_CHANNELS|0x0C|图像通道数|

#### 5.1.3 TUELEM\_TYPE 属性节点数据类型

|名称|代码|对应GenICam类型|
|-|-|-|
|TU\_ElemInteger|0x02|IInteger|
|TU\_ElemBoolean|0x03|IBoolean|
|TU\_ElemCommand|0x04|ICommand|
|TU\_ElemFloat|0x05|IFloat|
|TU\_ElemString|0x06|IString|
|TU\_ElemEnumeration|0x09|IEnumeration|

#### 5.1.4 TUACCESS\_MODE 访问权限

|名称|代码|描述|
|-|-|-|
|TU\_AM\_NI|0x00|未实现|
|TU\_AM\_NA|0x01|不可用|
|TU\_AM\_WO|0x02|只写|
|TU\_AM\_RO|0x03|只读|
|TU\_AM\_RW|0x04|读写|

#### 5.1.5 TU\_VISIBILITY 节点可见性

|名称|代码|描述|
|-|-|-|
|TU\_VS\_Beginner|0x00|全部可见|
|TU\_VS\_Expert|0x01|专家可见|
|TU\_VS\_Guru|0x02|高级可见|
|TU\_VS\_Invisible|0x03|隐藏|

#### 5.1.6 TUIMG\_FORMATS 图像保存格式

|名称|代码|描述|
|-|-|-|
|TUFMT\_RAW|0x01|自定义RAW格式|
|TUFMT\_TIF|0x02|TIF无损格式|
|TUFMT\_PNG|0x04|PNG格式|
|TUFMT\_JPG|0x08|JPG有损压缩|
|TUFMT\_BMP|0x10|BMP位图|

#### 5.1.7 TUFRM\_FORMATS 帧数据格式

|名称|代码|描述|
|-|-|-|
|TUFRM\_FMT\_RAW|0x10|RAW原始数据|
|TUFRM\_FMT\_USUAl|0x11|通用格式(8/16bit)|
|TUFRM\_FMT\_RGB888|0x12|RGB888 显示格式|

#### 5.1.8 TUDRAW\_MODE 绘制模式（Windows）

|名称|代码|描述|
|-|-|-|
|TUDRAW\_DFT|0x00|默认绘制|
|TUDRAW\_DIB|0x01|DIB绘制|
|TUDRAW\_DX9|0x02|DirectX9绘制|

### 5.2 结构体

#### 5.2.1 TUCAM\_INIT 初始化结构体

```C
typedef struct \_tagTUCAM\_INIT
{
    UINT32 uiCamCount;    // \[out] 在线相机数量
    PCHAR pstrConfigPath; // \[in] 配置文件路径
}TUCAM\_INIT, \*PTUCAM\_INIT;
```

#### 5.2.2 TUCAM\_OPEN 打开相机结构体

```C
typedef struct \_tagTUCAM\_OPEN
{
    UINT32 uiIdxOpen;     // \[in] 相机索引
    HDTUCAM hIdxTUCam;    // \[out] 相机句柄
}TUCAM\_OPEN, \*PTUCAM\_OPEN;
```

#### 5.2.3 TUCAM\_FRAME 帧结构体（核心）

```C
typedef struct \_tagTUCAM\_FRAME
{
    CHAR szSignature\[8];
    USHORT usHeader;      // 帧头长度
    USHORT usWidth;       // 图像宽度
    USHORT usHeight;      // 图像高度
    UINT32 uiWidthStep;   // 行步长
    UCHAR ucDepth;        // 位深
    UCHAR ucChannels;     // 通道数
    UCHAR ucElemBytes;    // 单像素字节数
    UCHAR ucFormatGet;    // \[in/out] 目标帧格式
    UINT32 uiImgSize;     // 图像数据大小
    UINT32 uiRsdSize;     // \[in] 一次读取帧数
    PUCHAR pBuffer;       // \[out] 帧数据缓冲区指针
}TUCAM\_FRAME, \*PTUCAM\_FRAME;
```

#### 5.2.4 TUCAM\_FILE\_SAVE 图片保存结构体

```C
typedef struct \_tagTUCAM\_FILE\_SAVE
{
    INT32 nSaveFmt;       // \[in] 保存格式 TUIMG\_FORMATS
    PCHAR pstrSavePath;   // \[in] 文件路径(无后缀)
    PTUCAM\_FRAME pFrame;  // \[in] 帧数据指针
}TUCAM\_FILE\_SAVE, \*PTUCAM\_FILE\_SAVE;
```

#### 5.2.5 TUCAM\_REC\_SAVE 录像保存结构体

```C
typedef struct \_tagTUCAM\_REC\_SAVE
{
    INT32 nCodec;         // \[in] 编码类型
    PCHAR pstrSavePath;   // \[in] 完整文件路径
    float fFps;           // \[in] 录制帧率
}TUCAM\_REC\_SAVE, \*PTUCAM\_REC\_SAVE;
```

### 5.3 函数

#### 5.3.1 API 初始化/反初始化

1. `TUCAMRET TUCAM\_Api\_Init(PTUCAM\_INIT pInit)`
功能：初始化整个SDK，枚举在线相机。
2. `TUCAMRET TUCAM\_Api\_Uninit()`
功能：反初始化SDK，释放全局资源，程序退出前调用。

#### 5.3.2 相机操作

1. `TUCAMRET TUCAM\_Dev\_Open(PTUCAM\_OPEN pOpenParam)`：打开指定索引相机，获取句柄。
2. `TUCAMRET TUCAM\_Dev\_Close(HDTUCAM hTUCam)`：关闭相机，释放设备资源。
3. `TUCAMRET TUCAM\_Dev\_GetInfo(HDTUCAM hTUCam, PTUCAM\_VALUE\_INFO pInfo)`：读取相机硬件/版本信息。

#### 5.3.3 GenICam 协议接口

1. `TUCAM\_GenICam\_ElementAttr`：查询单个属性节点信息。
2. `TUCAM\_GenICam\_ElementAttrNext`：遍历下一个属性节点。
3. `TUCAM\_GenICam\_SetElementValue`：设置属性值。
4. `TUCAM\_GenICam\_GetElementValue`：读取属性值。
5. `TUCAM\_GenICam\_BuffDataCallBack`：注册帧数据回调函数。
6. `TUCAM\_GenICam\_GetBuffData`：回调中读取原始帧数据。

#### 5.3.4 内存管理

1. `TUCAM\_Buf\_Alloc`：分配采集缓冲区。
2. `TUCAM\_Buf\_Release`：释放缓冲区。
3. `TUCAM\_Buf\_AbortWait`：终止帧等待阻塞。
4. `TUCAM\_Buf\_WaitForFrame`：阻塞等待一帧数据（核心取图接口）。
5. `TUCAM\_Buf\_CopyFrame`：拷贝为其他格式图像数据。

#### 5.3.5 采集控制

1. `TUCAM\_Cap\_Start(HDTUCAM hTUCam, UINT32 uiMode)`：启动图像采集。
2. `TUCAM\_Cap\_Stop(HDTUCAM hTUCam)`：停止图像采集。

#### 5.3.6 文件管理

1. `TUCAM\_File\_SaveImage`：保存单张图片。
2. `TUCAM\_Rec\_Start`：开启录像。
3. `TUCAM\_Rec\_AppendFrame`：追加视频帧。
4. `TUCAM\_Rec\_Stop`：停止录像。

#### 5.3.7 扩展寄存器操作

1. `TUCAM\_GenICam\_GetRegisterValue`：读取相机寄存器。
2. `TUCAM\_GenICam\_SetRegisterValue`：写入相机寄存器。

\---

## 6\. 常见问题解答(FAQ)

### 6.1 问题排查思路

1. 优先运行官方SamplePro测试对应功能，区分**硬件/SDK问题**和**自研代码问题**。
2. Sample正常、自研程序异常：排查代码逻辑、参数配置、资源释放顺序。
3. Sample也异常：参考下文问题方案，仍无法解决则收集版本信息、现象截图，联系技术支持。

### 6.2 常见问题及解决方案

1. **存图图像异常**
原因：`pBuffer` 包含帧头，未偏移。
解决：`uchar \*pBuf = m\_frame.pBuffer + m\_frame.usHeader;`
2. **重复调用 TUCAM\_Buf\_Alloc 报错(0x80000204)**
原因：缓冲区未释放重复分配。
解决：分配前先调用 `TUCAM\_Buf\_Release`。
3. **16bit 相机灰度值最大仅255**
原因：16bit像素为双字节存储，按单字节读取。
解决：强制转换为 `unsigned short\*` 解析数据。
4. **TUCAM\_Buf\_WaitForFrame 超时**
解决：调大接口超时参数，检查相机采集模式、触发配置。
5. **自定义参数设置不生效**
原因：GenICam协议相机不兼容旧 `TUCAM\_Capa\_/TUCAM\_Prop\_` 接口。
解决：使用 `TUCAM\_GenICam\_` 系列接口配置参数。
6. **自动色阶设置无效**
解决：先开启直方图功能，且需在 `TUCAM\_Cap\_Start` 之后配置参数。

> （注：文档部分内容可能由 AI 生成）
