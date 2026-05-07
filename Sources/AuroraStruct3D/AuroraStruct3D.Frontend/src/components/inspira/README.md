# Inspira UI 组件目录

本目录用于存放从 [Inspira UI](https://inspira-ui.com/) 注册表添加的组件。

## 添加方式

```powershell
# 示例：添加 Scales 动效组件
npx shadcn-vue@latest add "https://registry.inspira-ui.com/scales.json"

# 示例：添加 Aurora 背景
npx shadcn-vue@latest add "https://registry.inspira-ui.com/aurora-background.json"
```

执行后组件会被写入 `src/components/inspira/`（受 `components.json` 中的 alias 控制）。

## 引用约定

页面/视图禁止直接使用原生 HTML 控件，所有动效组件统一从：

```ts
import { XxxComponent } from '@/components/inspira/xxx'
```

导入。
