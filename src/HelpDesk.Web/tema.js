// Aplica el tema (claro u oscuro) antes de que se pinte la página, para que nunca aparezca
// el tema equivocado al cargar. Se carga sin "defer" desde <head> (la CSP no permite scripts en línea).
// Preferencia guardada por el usuario; si no eligió ninguna, la del sistema operativo.
(function () {
  var guardado = null;
  try {
    guardado = localStorage.getItem('helpdesk.tema');
  } catch (e) {
    // Almacenamiento bloqueado: se usa la preferencia del sistema.
  }
  var oscuro =
    guardado === 'oscuro' ||
    (guardado !== 'claro' && !!window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches);
  document.documentElement.setAttribute('data-tema', oscuro ? 'oscuro' : 'claro');
})();
