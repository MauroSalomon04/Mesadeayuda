import { useCallback, useEffect, useState } from 'react';

// Tema claro u oscuro. tema.js (en <head>) ya lo aplicó antes de pintar; aquí se alterna y se guarda.

export type Tema = 'claro' | 'oscuro';

const CLAVE = 'helpdesk.tema';
const CONSULTA_OSCURO = '(prefers-color-scheme: dark)';

function guardado(): Tema | null {
  try {
    const valor = localStorage.getItem(CLAVE);
    return valor === 'claro' || valor === 'oscuro' ? valor : null;
  } catch {
    return null;
  }
}

const delSistema = (): Tema => (window.matchMedia?.(CONSULTA_OSCURO).matches ? 'oscuro' : 'claro');

function actual(): Tema {
  const atributo = document.documentElement.getAttribute('data-tema');
  return atributo === 'claro' || atributo === 'oscuro' ? atributo : guardado() ?? delSistema();
}

export function useTema() {
  const [tema, setTema] = useState<Tema>(actual);

  useEffect(() => {
    document.documentElement.setAttribute('data-tema', tema);
  }, [tema]);

  useEffect(() => {
    // Mientras el usuario no elija un tema, se sigue al sistema operativo.
    const consulta = window.matchMedia?.(CONSULTA_OSCURO);
    const alCambiarSistema = () => {
      if (!guardado()) setTema(delSistema());
    };
    // Otra pestaña cambió el tema: se aplica también en esta.
    const alCambiarOtraPestana = (e: StorageEvent) => {
      if (e.key === CLAVE) setTema(guardado() ?? delSistema());
    };
    consulta?.addEventListener('change', alCambiarSistema);
    window.addEventListener('storage', alCambiarOtraPestana);
    return () => {
      consulta?.removeEventListener('change', alCambiarSistema);
      window.removeEventListener('storage', alCambiarOtraPestana);
    };
  }, []);

  const alternar = useCallback(() => {
    setTema((anterior) => {
      const nuevo: Tema = anterior === 'oscuro' ? 'claro' : 'oscuro';
      try {
        localStorage.setItem(CLAVE, nuevo);
      } catch {
        // Sin almacenamiento: el cambio vale hasta cerrar la pestaña.
      }
      return nuevo;
    });
  }, []);

  return { tema, alternar };
}
