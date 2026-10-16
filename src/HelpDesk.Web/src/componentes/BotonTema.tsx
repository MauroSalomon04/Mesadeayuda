import { LuMoon, LuSun } from 'react-icons/lu';
import { useTema } from '../estado/tema';

/** Alterna entre modo claro y oscuro (la elección se recuerda en este navegador). */
export function BotonTema({ className = '' }: { className?: string }) {
  const { tema, alternar } = useTema();
  const oscuro = tema === 'oscuro';
  const texto = oscuro ? 'Cambiar a modo claro' : 'Cambiar a modo oscuro';
  return (
    <button type="button" className={`btn btn-sutil btn-icono boton-tema ${className}`} onClick={alternar} title={texto} aria-label={texto} aria-pressed={oscuro}>
      {oscuro ? <LuSun aria-hidden /> : <LuMoon aria-hidden />}
    </button>
  );
}
