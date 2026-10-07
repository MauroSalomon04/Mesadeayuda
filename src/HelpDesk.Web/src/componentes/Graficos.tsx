import { MouseEvent, useState } from 'react';
import type { Conteo } from '../tipos';
import { nombreMes, numero } from '../util/formato';

interface Tip {
  x: number;
  y: number;
  texto: string;
}

function useTooltip() {
  const [tip, setTip] = useState<Tip | null>(null);
  const mostrar = (texto: string) => (e: MouseEvent) => setTip({ x: e.clientX, y: e.clientY, texto });
  const ocultar = () => setTip(null);
  const nodo = tip ? (
    <div className="tooltip" style={{ left: tip.x, top: tip.y }} role="tooltip">
      {tip.texto}
    </div>
  ) : null;
  return { mostrar, ocultar, nodo };
}

/** Barras horizontales de una sola serie (cantidad por categoría). */
export function BarrasHorizontales({ datos, total, alElegir }: { datos: Conteo[]; total?: number; alElegir?: (c: Conteo) => void }) {
  const { mostrar, ocultar, nodo } = useTooltip();
  if (datos.length === 0) return <p className="tenue">Sin datos en el período.</p>;
  const maximo = Math.max(...datos.map((d) => d.cantidad), 1);
  const suma = total ?? datos.reduce((s, d) => s + d.cantidad, 0);
  return (
    <>
      <div className="barras">
        {datos.map((d) => {
          const porcentaje = suma > 0 ? (100 * d.cantidad) / suma : 0;
          const texto = `${d.etiqueta}: ${numero(d.cantidad)} (${porcentaje.toFixed(1).replace('.', ',')} %)`;
          return (
            <div
              key={d.etiqueta}
              className="barra-fila"
              onMouseMove={mostrar(texto)}
              onMouseLeave={ocultar}
              onClick={alElegir ? () => alElegir(d) : undefined}
              style={alElegir ? { cursor: 'pointer' } : undefined}
            >
              <span className="barra-etiqueta" title={d.etiqueta}>
                {d.etiqueta}
              </span>
              <span className="barra-pista">
                <span className="barra" style={{ display: 'block', width: `${(100 * d.cantidad) / maximo}%` }} />
              </span>
              <span className="barra-valor">{numero(d.cantidad)}</span>
            </div>
          );
        })}
      </div>
      {nodo}
    </>
  );
}

function pasoLindo(maximo: number) {
  const bruto = maximo / 4;
  const magnitud = Math.pow(10, Math.floor(Math.log10(Math.max(bruto, 1))));
  const normal = bruto / magnitud;
  const paso = normal <= 1 ? 1 : normal <= 2 ? 2 : normal <= 2.5 ? 2.5 : normal <= 5 ? 5 : 10;
  return paso * magnitud;
}

/** Columnas por mes (una sola serie). Rotula el máximo y el último mes; el resto va en el tooltip. */
export function ColumnasMes({ datos }: { datos: { mes: string; cantidad: number; pendientes: number }[] }) {
  const { mostrar, ocultar, nodo } = useTooltip();
  if (datos.length === 0) return <p className="tenue">Sin datos en el período.</p>;
  const maximoDato = Math.max(...datos.map((d) => d.cantidad), 1);
  const paso = pasoLindo(maximoDato);
  const tope = Math.ceil(maximoDato / paso) * paso;
  const guias: number[] = [];
  for (let v = 0; v <= tope; v += paso) guias.push(v);
  const indiceMaximo = datos.findIndex((d) => d.cantidad === maximoDato);
  const corto = datos.length > 8;

  return (
    <div>
      <div className="columnas" role="img" aria-label="Solicitudes por mes">
        <div className="columnas-guias" aria-hidden>
          {guias.map((g) => (
            <div key={g} className="columnas-guia" style={{ bottom: `${(100 * g) / tope}%` }}>
              <span>{numero(g)}</span>
            </div>
          ))}
        </div>
        {datos.map((d, i) => {
          const rotular = i === indiceMaximo || i === datos.length - 1;
          const texto = `${nombreMes(d.mes)}: ${numero(d.cantidad)} solicitudes${d.pendientes ? `, ${numero(d.pendientes)} pendientes` : ''}`;
          return (
            <div key={d.mes} className="columna" onMouseMove={mostrar(texto)} onMouseLeave={ocultar}>
              <div className="columna-barra" style={{ height: `${(100 * d.cantidad) / tope}%` }} />
              {rotular && (
                <span className="columna-valor" style={{ bottom: `${(100 * d.cantidad) / tope}%` }}>
                  {numero(d.cantidad)}
                </span>
              )}
            </div>
          );
        })}
      </div>
      <div className="columnas-ejes" aria-hidden>
        {datos.map((d) => (
          <span key={d.mes}>{nombreMes(d.mes, corto)}</span>
        ))}
      </div>
      <table className="oculto">
        <caption>Solicitudes por mes</caption>
        <tbody>
          {datos.map((d) => (
            <tr key={d.mes}>
              <th>{nombreMes(d.mes)}</th>
              <td>{d.cantidad}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {nodo}
    </div>
  );
}
