import { defineConfig } from 'vitepress'

export default defineConfig({
  lang: 'zh-CN',
  title: 'Aurora 2.0',
  description: 'Aurora 工业控制上位机框架文档',
  cleanUrls: true,
  themeConfig: {
    nav: [
      { text: '架构', link: '/architecture' },
      { text: '通讯', link: '/communication' },
      { text: '安全', link: '/security' },
      { text: '迁移', link: '/migration' }
    ],
    sidebar: [
      { text: '开始', items: [{ text: '本地运行', link: '/getting-started' }] },
      { text: '设计', items: [
        { text: '架构总览', link: '/architecture' },
        { text: 'AuroraCommunication', link: '/communication' },
        { text: '控制与安全', link: '/security' },
        { text: '从 1.x 迁移', link: '/migration' }
      ] }
    ],
    search: { provider: 'local' }
  }
})
