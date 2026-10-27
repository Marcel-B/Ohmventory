import { createRouter, createWebHistory } from 'vue-router'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/lager' },
    { path: '/lager', name: 'inventory', component: () => import('./views/InventoryView.vue'), meta: { title: 'Lager' } },
    { path: '/erfassen', name: 'capture', component: () => import('./views/CaptureView.vue'), meta: { title: 'Erfassen' } },
    { path: '/containers', name: 'containers', component: () => import('./views/ContainersView.vue'), meta: { title: 'Container' } },
    { path: '/containers/:code', name: 'container', component: () => import('./views/ContainerDetailView.vue'), props: true, meta: { title: 'Container' } },
    { path: '/assistent', name: 'assistant', component: () => import('./views/AssistantView.vue'), meta: { title: 'Projektassistent' } },
    { path: '/projekte', name: 'projects', component: () => import('./views/ProjectsView.vue'), meta: { title: 'Projekte' } },
    { path: '/einkaufsliste', name: 'shopping', component: () => import('./views/ShoppingListView.vue'), meta: { title: 'Einkaufsliste' } },
  ],
})

router.afterEach((to) => {
  document.title = to.meta.title ? `${to.meta.title} · Ohmventory` : 'Ohmventory'
})
