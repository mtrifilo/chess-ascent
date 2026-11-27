// Full page loads, for when the session changes underneath the app. A
// client-side navigation keeps everything the app holds in the browser: the
// query cache, and the pages Next.js keeps in the document, hidden, so that
// going back restores them (form fields included). A full load drops all of
// it, so nothing held for one account is shown to whoever uses the browser
// next.

export function loadPage(path: string) {
  window.location.assign(path)
}

export function reloadPage() {
  window.location.reload()
}
