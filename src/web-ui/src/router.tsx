import React from 'react';
import { ActionsListPage } from './pages/actions/ActionsListPage';
import { CreateActionPage } from './pages/actions/CreateActionPage';
import { EditActionPage } from './pages/actions/EditActionPage';

export type RouteConfig = {
  path: string;
  element: React.ReactNode;
  name: string;
};

export const actionRoutes: RouteConfig[] = [
  { path: '/actions', element: <ActionsListPage />, name: 'Actions List' },
  { path: '/actions/new', element: <CreateActionPage />, name: 'Create Action' },
  { path: '/actions/:id/edit', element: <EditActionPage />, name: 'Edit Action' }
];

export const appRoutes: RouteConfig[] = [...actionRoutes];
