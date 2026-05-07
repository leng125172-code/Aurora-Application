/**
 * shadcn-vue 标准 className 合并工具：clsx + tailwind-merge
 * 用法：cn('px-2', condition && 'bg-red-500')
 */
import { clsx, type ClassValue } from 'clsx'
import { twMerge } from 'tailwind-merge'

export function cn(...inputs: ClassValue[]): string {
    return twMerge(clsx(inputs))
}
