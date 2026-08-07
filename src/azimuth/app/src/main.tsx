import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { Shell } from './shell/Shell';
import { applyTheme, DARK } from './theme/sentinel';
import { log } from './data/api';
import './styles/sentinel.css';

applyTheme(DARK);
log.push('ok', 'Sentinel Azimuth v0.1.0 — shell up');

const el = document.getElementById('root');
if (!el) throw new Error('#root missing');
createRoot(el).render(
  <StrictMode>
    <Shell />
  </StrictMode>,
);

// Shell-spike instrumentation: report first paint so spawn→interactive can be
// measured from outside the process. Harmless if the sidecar is not up.
requestAnimationFrame(() => {
  fetch('/api/_boot').catch(() => {});
});
