/** Reloads the whole page. A named function, so tests can replace it. */
export const reloadPage = (): void => {
  window.location.reload();
};
