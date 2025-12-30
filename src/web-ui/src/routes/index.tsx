import React from 'react';
import { ExecutionPage } from '../pages/Execution';
import { navigationAreas } from '../types/navigation';

export const executionRoute = {
  id: 'execution',
  element: <ExecutionPage />,
  path: '/execution'
};

export const appNavigationAreas = navigationAreas;