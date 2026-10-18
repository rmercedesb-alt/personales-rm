import { useEffect, useMemo, useRef, useState } from "react";
import type { Vendedor } from "../types";

interface Props {
  vendedores: Vendedor[];
  selected: Vendedor | null;
  onSelect: (v: Vendedor) => void;
}

export function VendedorPicker({ vendedores, selected, onSelect }: Props) {
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
    const q = query.trim().toLowerCase();
    const base = q
      ? vendedores.filter((v) => `${v.repCodigo} ${v.repNombre}`.toLowerCase().includes(q))
      : vendedores;
    return base.slice(0, 200);
  }, [vendedores, query]);

  return (
    <div className="ms" ref={ref}>
      <button className="ms-btn vendedor-btn" onClick={() => setOpen((o) => !o)}>
        {selected ? `${selected.repCodigo} - ${selected.repNombre}` : "Elegir vendedor…"} ▾
      </button>
      {open && (
        <div className="ms-panel vendedor-panel">
          <input
            className="ms-search"
            placeholder={`Buscar en ${vendedores.length} vendedores…`}
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            autoFocus
          />
          {visible.map((v) => (
            <div
              key={v.repCodigo}
              className={`vendedor-row ${selected?.repCodigo === v.repCodigo ? "active" : ""}`}
              onClick={() => {
                onSelect(v);
                setOpen(false);
                setQuery("");
              }}
            >
              <div className="vendedor-nombre">
                {v.repCodigo} - {v.repNombre}
              </div>
              <div className="vendedor-meta">
                {v.supNombre} · {v.cedDescripcion}
              </div>
            </div>
          ))}
          {visible.length === 0 && <div className="ms-hint">Sin resultados</div>}
        </div>
      )}
    </div>
  );
}
