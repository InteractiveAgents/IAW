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
            { text: 'Message Types', link: '/guide/messages' },
            { text: 'Events & Streams', link: '/guide/events-streams' },
            { text: 'Communication', link: '/guide/communication' },
            { text: 'LLM Agents', link: '/guide/llm-agents' },
            { text: 'Persistence', link: '/guide/persistence' }
          ]
        },
        {
          text: 'Behaviors',
          items: [
            { text: 'Conversation', link: '/guide/behaviors/conversation' },
            { text: 'Tools', link: '/guide/behaviors/tools' },
            { text: 'Tracking', link: '/guide/behaviors/tracking' }
          ]
        },
        {
          text: 'Advanced',
          items: [
            { text: 'Orchestration', link: '/guide/orchestration' },
            { text: 'Consilium', link: '/guide/consilium' },
            { text: 'Memory', link: '/guide/memory' },
            { text: 'Task Supervision', link: '/guide/supervisor' }
          ]
        },
        {
          text: 'Integrations',
          items: [
            { text: 'MCP Server', link: '/guide/mcp' },
            { text: 'Telegram Bot', link: '/guide/telegram' },
            { text: 'Telegram Features', link: '/guide/telegram-features' },
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
        },
        {
          text: 'Use Cases',
          items: [
            { text: 'Code Review Bot', link: '/tutorials/use-cases/code-review-bot' },
            { text: 'Infrastructure Monitor', link: '/tutorials/use-cases/infra-monitor' },
            { text: 'Personal Assistant', link: '/tutorials/use-cases/personal-assistant' },
            { text: 'Knowledge Base', link: '/tutorials/use-cases/knowledge-base' },
            { text: 'CI/CD Pipeline', link: '/tutorials/use-cases/cicd-pipeline' }
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
