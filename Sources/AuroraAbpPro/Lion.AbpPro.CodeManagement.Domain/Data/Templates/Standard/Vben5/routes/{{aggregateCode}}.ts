import type { RouteRecordRaw } from 'vue-router';

import { BasicLayout } from '#/layouts';
import { $t } from '#/locales';

const routes: RouteRecordRaw[] = [
    {
        component: BasicLayout,
        meta: {
            icon: 'ant-design:switcher-filled',
            //order: 998,
            title: '{{ context.EntityModel.Description }}管理',
            //authority: ['{{ context.EntityModel.AggregateCode }}Permissions.GroupName'],
        },
        name: '{{ context.EntityModel.AggregateCode }}',
        path: '/{{ context.EntityModel.AggregateCode }}',
        children: [
            {
                path: 'page',
                name: '{{ context.EntityModel.AggregateCode }}Page',
                component: () => import('#/views/{{ context.EntityModel.AggregateCodePluralized }}/index.vue'),
                meta: {
                    icon: 'ph:user',
                    title: '{{ context.EntityModel.Description }}列表',
                    //authority: ['{{ context.EntityModel.AggregateCode }}.{{ context.EntityModel.AggregateCode }}Management.Default'],
                },
            }
        ],
    },
];

export default routes;
