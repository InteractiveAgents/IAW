import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'Interactive Agents',
  description: 'An open-source ecosystem of intelligent agents built on Orleans and .NET',
  base: '/IAW/',

  head: [
    ['link', { rel: 'icon', type: 'image/svg+xml', href: '/IAW/logo.svg' }]
  ],

  themeConfig: {
    logo: '/logo.svg',

    nav: [
      { text: 'Guide', link: '/guide/' },
      { text: 'Tutorials', link: '/tutorials/first-agent' },
      { text: 'Reference', link: '/reference/' }
    ],

    sidebar: {
      '/guide/': [
        {
          text: 'Introduction',
          items: [
            { text: 'Getting Started', link: '/guide/' }
          ]
        },
        {
          text: 'Core Concepts',
          items: [
            { text: 'Architecture', link: '/guide/architecture' },
            { text: 'Building Agents', link: '/guide/agents' },
            { text: 'Notifications & Events', link: '/guide/notifications' }
          ]
        },
        {
          text: 'Integrations',
          items: [
            { text: 'MCP Server', link: '/guide/mcp' },
            { text: 'Telegram Bot', link: '/guide/telegram' },
            { text: 'Testing', link: '/guide/testing' }
          ]
        }
      ],
      '/tutorials/': [
        {
          text: 'Tutorials',
          items: [
            { text: 'Build Your First Agent', link: '/tutorials/first-agent' }
          ]
        }
      ],
      '/reference/': [
        {
          text: 'API Reference',
          items: [
            { text: 'Overview', link: '/reference/' },
            { text: 'Configuration', link: '/reference/configuration' }
          ]
        }
      ]
    },

    socialLinks: [
      { icon: 'github', link: 'https://github.com/InteractiveAgents/IAW' }
    ],

    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright 2026 InteractiveAgents'
    },

    search: {
      provider: 'local'
    }
  }
})
