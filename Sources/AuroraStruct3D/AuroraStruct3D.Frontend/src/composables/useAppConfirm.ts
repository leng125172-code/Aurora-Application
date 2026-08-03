import { useConfirm } from 'primevue/useconfirm'

export interface AppConfirmOptions {
    message: string
    header?: string
    acceptLabel?: string
    rejectLabel?: string
    danger?: boolean
}

/** Promise 形式的全局确认框，统一替代浏览器原生 confirm。 */
export function useAppConfirm() {
    const confirm = useConfirm()

    return (options: AppConfirmOptions): Promise<boolean> =>
        new Promise((resolve) => {
            let settled = false
            const finish = (result: boolean) => {
                if (settled) return
                settled = true
                resolve(result)
            }
            confirm.require({
                group: 'global',
                header: options.header ?? '请确认',
                message: options.message,
                icon: options.danger === false ? 'pi pi-info-circle' : 'pi pi-exclamation-triangle',
                acceptLabel: options.acceptLabel ?? '确认',
                rejectLabel: options.rejectLabel ?? '取消',
                acceptClass: options.danger === false ? undefined : 'p-button-danger',
                accept: () => finish(true),
                reject: () => finish(false),
                onHide: () => finish(false),
            })
        })
}
