import React from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { applyThemeTokens } from './theme/tokens';
import './theme/global.css';
import './styles.css';

applyThemeTokens();

createRoot(document.getElementById('root')!).render(<App />);