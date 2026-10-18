import { useEffect, useMemo, useRef, useState } from "react";

interface Props {
  label: string;
  options: string[];
  selected: Set<string>;
  onChange: (next: Set<string>) => void;
  searchable?: boolean;
  /** Si es false, una selección vacía se muestra como "Ninguno" en vez de "Todos" (para multiselects que cargan datos extra, no que filtran). */
  emptyMeansAll?: boolean;
}

const RENDER_CAP = 200;

export function Multiselect({ label, options, selected, onChange, searchable, emptyMeansAll = true }: Props) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function onDocClick(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    }
    document.addEventListener("mousedown", onDocClick);
    return () => document.removeEventListener("mousedown", onDocClick);
  }, []);

  const visible = useMemo(() => {
    if (!query.trim()) return options;
    const q = query.trim().toLowerCase();
    return options.filter((o) => o.toLowerCase().includes(q));
  }, [options, query]);

  const rendered = visible.slice(0, RENDER_CAP);

  const allSelected = options.length > 0 && options.every((o) => selected.has(o));
  const summary =
    selected.size === 0
      ? `${label}: ${emptyMeansAll ? "Todos" : "Ninguno"}`
      : allSelected && emptyMeansAll
        ? `${label}: Todos`
        : selected.size === 1
          ? `${label}: ${[...selected][0]}`
          : `${label}: ${selected.size} de ${options.length}`;

  function toggle(opt: string) {
    const next = new Set(selected);
    if (next.has(opt)) next.delete(opt);
    else next.add(opt);
    onChange(next);
  }

  const showSearch = searchable ?? options.length > 8;

  return (
    <div className="ms" ref={ref}>
      <button className="ms-btn" onClick={() => setOpen((o) => !o)}>
        {summary} ▾
      </button>
      {open && (
        <div className="ms-panel">
          {showSearch && (
            <input
              className="ms-search"
              placeholder={`Buscar en ${options.length}…`}
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              autoFocus
            />
          )}
          <div className="ms-actions">
            <button onClick={() => onChange(new Set(visible))}>✓ Todos</button>
            <button onClick={() => onChange(new Set())}>✕ Ninguno</button>
          </div>
          {rendered.map((opt) => (
            <label key={opt}>
              <input type="checkbox" checked={selected.has(opt)} onChange={() => toggle(opt)} />
              {opt}
            </label>
          ))}
          {visible.length > RENDER_CAP && (
            <div className="ms-hint">
              Mostrando {RENDER_CAP} de {visible.length} — refine la búsqueda
            </div>
          )}
          {visible.length === 0 && <div className="ms-hint">Sin resultados</div>}
        </div>
      )}
    </div>
  );
}
