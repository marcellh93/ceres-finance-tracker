import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { HashRouter } from 'react-router-dom';
import '../index.css';
import { App } from './App';
import { ThemeProvider } from '../app/theme/theme-context';

const root = document.getElementById('root');
if (!root) throw new Error('design-system root element missing');

createRoot(root).render(
  <StrictMode>
    <ThemeProvider>
      <HashRouter>
        <App />
      </HashRouter>
    </ThemeProvider>
  </StrictMode>,
);
