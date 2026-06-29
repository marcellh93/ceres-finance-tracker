import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import '../index.css';
import './i18n/i18n';
import { App } from './App';
import { AuthProvider } from './auth/auth-context';
import { ThemeProvider } from './theme/theme-context';

const root = document.getElementById('root');
if (!root) throw new Error('app root element missing');

createRoot(root).render(
  <StrictMode>
    <ThemeProvider>
      <BrowserRouter>
        <AuthProvider>
          <App />
        </AuthProvider>
      </BrowserRouter>
    </ThemeProvider>
  </StrictMode>,
);
