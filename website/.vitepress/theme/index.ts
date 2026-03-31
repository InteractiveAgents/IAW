import DefaultTheme from 'vitepress/theme'
import BehaviorTabs from './BehaviorTabs.vue'
import ArchitectureDiagram from './components/ArchitectureDiagram.vue'
import './custom.css'

export default {
  extends: DefaultTheme,
  enhanceApp({ app }) {
    app.component('BehaviorTabs', BehaviorTabs)
    app.component('ArchitectureDiagram', ArchitectureDiagram)
  }
}
