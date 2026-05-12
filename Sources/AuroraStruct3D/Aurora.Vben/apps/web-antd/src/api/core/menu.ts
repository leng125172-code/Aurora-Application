/**
 * 获取用户所有菜单
 */
export async function getAllMenusApi() {
  const dashboardMenus = [
    {
      meta: {
        order: -1,
        title: 'page.dashboard.title',
      },
      name: 'Dashboard',
      path: '/dashboard',
      redirect: '/analytics',
      children: [
        {
          name: 'Analytics',
          path: '/analytics',
          component: '/dashboard/analytics/index',
          meta: {
            affixTab: true,
            title: 'page.dashboard.analytics',
            authority: ['FileManagement.File1'],
          },
        },
        {
          name: 'Workspace',
          path: '/workspace',
          component: '/dashboard/workspace/index',
          meta: {
            title: 'page.dashboard.workspace',
          },
        },
      ],
    },
  ];
  debugger;
  return dashboardMenus;
  // return requestClient.get<RouteRecordStringComponent[]>('/menu/all');
}
