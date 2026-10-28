import React from 'react';
import ReactDOM from 'react-dom/client';
import { createBrowserRouter, RouterProvider } from 'react-router-dom';
import './theme.css';
import App from './App';
import { NavigationGuardProvider } from './components/NavigationGuard';

// A root splat keeps the existing route tree while enabling React Router's
// navigation blocker for links, programmatic navigation and browser history.
const router = createBrowserRouter([
  { path: '*', element: <NavigationGuardProvider><App /></NavigationGuardProvider> },
]);

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <RouterProvider router={router} />
  </React.StrictMode>,
);
