/**
 * 全局 Toast 组合函数：基于 PrimeVue Toast 服务封装
 *
 * 设计目的：
 *  1. 业务侧统一通过 useAppToast() 调用 Toast，便于后续切换底层实现
 *  2. 提供与原 vue-sonner `toast.success/error/warn/info` 一致的链式 API，
 *     便于现有 30+ 业务页面零成本机械替换
 *  3. 默认 3000ms 自动关闭；错误类默认 5000ms，便于阅读异常信息
 *
 * 使用示例：
 *   const toast = useAppToast()
 *   toast.success('保存成功')
 *   toast.error(err)
 *   toast.warn('请填写必填项')
 *   toast.info('已加入后台任务队列')
 *
 * 注意：必须在 setup() 同步上下文中调用，因 PrimeVue useToast 通过 inject 获取服务
 */
import { useToast } from 'primevue/usetoast'

/** Toast 严重程度，对齐 PrimeVue ToastMessageOptions['severity'] */
type ToastSeverity = 'success' | 'info' | 'warn' | 'error' | 'secondary' | 'contrast'

/** Toast 可选参数 */
export interface AppToastOptions {
  /** 标题，可选；不传则按严重程度使用默认中文标题 */
  title?: string
  /** 自动关闭毫秒数；不传则按严重程度使用默认值；显式传 0 表示不自动关闭 */
  duration?: number
  /** 唯一分组（用于覆盖/避免重复，目前 PrimeVue 通过 group 区分容器，可不传） */
  group?: string
}

/** 严重程度 → 默认中文标题 */
const DEFAULT_TITLES: Record<ToastSeverity, string> = {
  success: '成功',
  info: '提示',
  warn: '警告',
  error: '错误',
  secondary: '消息',
  contrast: '消息',
}

/** 严重程度 → 默认显示毫秒数 */
const DEFAULT_LIFE: Record<ToastSeverity, number> = {
  success: 3000,
  info: 3000,
  warn: 4000,
  error: 5000,
  secondary: 3000,
  contrast: 3000,
}

/**
 * 将任意输入归一化为可读字符串：
 *  - Error 实例取 message
 *  - 对象走 JSON.stringify（失败时 fallback 到 String()）
 *  - 其余 String() 兜底
 */
function normalizeMessage(input: unknown): string {
  if (input == null) return ''
  if (typeof input === 'string') return input
  if (input instanceof Error) return input.message
  if (typeof input === 'object') {
    try {
      return JSON.stringify(input)
    } catch {
      return String(input)
    }
  }
  return String(input)
}

/**
 * 全局 Toast 组合函数
 *
 * @returns Toast API 对象，提供 success / error / warn / info / show / clear 方法
 */
export function useAppToast() {
  const toast = useToast()

  /** 内部统一 add 方法 */
  function add(severity: ToastSeverity, message: unknown, options?: AppToastOptions): void {
    const summary = options?.title ?? DEFAULT_TITLES[severity]
    const life = options?.duration ?? DEFAULT_LIFE[severity]
    toast.add({
      severity,
      summary,
      detail: normalizeMessage(message),
      life: life > 0 ? life : undefined,
      group: options?.group,
    })
  }

  return {
    /** 成功提示 */
    success(message: unknown, options?: AppToastOptions): void {
      add('success', message, options)
    },
    /** 错误提示（默认展示 5 秒） */
    error(message: unknown, options?: AppToastOptions): void {
      add('error', message, options)
    },
    /** 警告提示（兼容 vue-sonner `toast.warning`） */
    warn(message: unknown, options?: AppToastOptions): void {
      add('warn', message, options)
    },
    /** 警告提示别名，方便从 vue-sonner 直接迁移 */
    warning(message: unknown, options?: AppToastOptions): void {
      add('warn', message, options)
    },
    /** 普通信息提示 */
    info(message: unknown, options?: AppToastOptions): void {
      add('info', message, options)
    },
    /** 透传任意 severity */
    show(severity: ToastSeverity, message: unknown, options?: AppToastOptions): void {
      add(severity, message, options)
    },
    /** 清空所有 Toast；可选传入 group 仅清空指定分组 */
    clear(group?: string): void {
      if (group) toast.removeGroup(group)
      else toast.removeAllGroups()
    },
  }
}

/** Toast API 类型，便于在业务侧导入参数 */
export type AppToast = ReturnType<typeof useAppToast>
