import { useCallback, useRef } from 'react';
import { Actions, Layout, Model, type TabNode } from 'flexlayout-react';
import 'flexlayout-react/style/dark.css';
import { Explorer } from '../panes/Explorer';
import { ChartPane } from '../panes/ChartPane';
import { Analyzer } from '../panes/Analyzer';
import { Terminal } from '../panes/Terminal';
import { StatusBar } from './StatusBar';
import { SelectionProvider } from './selection';
import { DEFAULT_LAYOUT } from './layout';

const VERSION = 'v0.1.0';

/**
 * The dockable shell (spec §7.1).
 *
 * FlexLayout gives the VS Code / Quant Charts docking model: drag a tab to any
 * edge to split, drag between tabsets to re-dock, and pop a tab into its own
 * OS window. Panes are registered by component key so a popped-out window
 * constructs the same component — which is why shared state lives in
 * SelectionProvider rather than inside a pane.
 */
export function Shell() {
  const modelRef = useRef<Model>(Model.fromJson(DEFAULT_LAYOUT));
  const layoutRef = useRef<Layout>(null);

  const factory = useCallback((node: TabNode) => {
    switch (node.getComponent()) {
      case 'explorer': return <Explorer />;
      case 'chart':    return <ChartPane />;
      case 'analyzer': return <Analyzer />;
      case 'terminal': return <Terminal />;
      default:
        return <div className="empty">unknown pane “{node.getComponent()}”</div>;
    }
  }, []);

  const reset = () => {
    modelRef.current = Model.fromJson(DEFAULT_LAYOUT);
    // force a remount of the layout tree
    layoutRef.current?.forceUpdate();
    window.location.reload();
  };

  return (
    <SelectionProvider>
      <div className="app">
        <header className="hdr">
          {/* the cyan eye — LIVE/watching, the suite's signature */}
          <span className="hdr__eye" />
          <span className="hdr__mark">SENTINEL</span>
          <span className="hdr__tool">AZIMUTH</span>
          <span className="hdr__chip">{VERSION}</span>
          <span className="hdr__spacer" />
          <span className="pill pill--warn" title="v1 is read-only research; the Azimuth does not route orders (spec §1.1)">
            RESEARCH · NO ORDER ROUTING
          </span>
          <button className="btn" onClick={reset} title="Restore the default pane layout">
            RESET LAYOUT
          </button>
        </header>

        <div className="app__body">
          <Layout
            ref={layoutRef}
            model={modelRef.current}
            factory={factory}
            onModelChange={(m) => {
              try {
                localStorage.setItem('azimuth.layout', JSON.stringify(m.toJson()));
              } catch { /* non-fatal */ }
            }}
            popoutURL="./index.html"
            realtimeResize
          />
        </div>

        <StatusBar />
      </div>
    </SelectionProvider>
  );
}

export { Actions };
