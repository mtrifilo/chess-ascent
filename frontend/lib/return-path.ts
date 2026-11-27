// Where a visitor goes after signing in or signing up. The sign-in and
// sign-up pages carry it in their address as `?next=<path>`, so a visitor
// sent to sign in from a page that needs an account comes back to that page.

export const RETURN_PATH_PARAM = 'next'

// Where a visitor goes when no return path is given, or the one given is
// refused.
const DEFAULT_RETURN_PATH = '/play'

// A stand-in origin to resolve a candidate path against. It is never
// contacted: anything that resolves to another origin is refused, and only
// the path, query and fragment of what remains are kept.
const THIS_SITE = 'https://this-site.invalid'

// The path within this site that `value` names, or DEFAULT_RETURN_PATH when
// it names anything else. The value comes from the address bar, so anyone can
// write a link to /sign-in that carries one; following an address on another
// site after signing in would make this site an open redirect.
//
// The check is made by a URL parser, as the browser's own, rather than by
// matching characters: browsers read `//evil.example`, `/\evil.example` and
// `/<tab>/evil.example` all as addresses on another site, and the parser
// resolves each of them to that site's origin.
//
// The path that comes out is checked too, because resolving can change it:
// dot segments are removed, so `/.//evil.example` resolves on this site to the
// path `//evil.example`, which the browser, given it as an address, reads as
// another site. Only a path that resolves to this site a second time is kept.
export function returnPathFrom(value: string | null | undefined): string {
  if (!value?.startsWith('/')) return DEFAULT_RETURN_PATH

  const path = pathOnThisSite(value)
  if (path === undefined || pathOnThisSite(path) !== path) {
    return DEFAULT_RETURN_PATH
  }
  return path
}

// The path, query and fragment `value` resolves to, or undefined when it
// resolves to another origin or does not parse.
function pathOnThisSite(value: string): string | undefined {
  let url: URL
  try {
    url = new URL(value, THIS_SITE)
  } catch {
    return undefined
  }
  if (url.origin !== THIS_SITE) return undefined
  return url.pathname + url.search + url.hash
}

// The address of the sign-in or sign-up page that returns the visitor to
// `returnTo` afterwards. Without one, the page's own address.
export function authPagePath(
  page: '/sign-in' | '/sign-up',
  returnTo?: string
): string {
  if (returnTo === undefined) return page
  const query = new URLSearchParams({ [RETURN_PATH_PARAM]: returnTo })
  return `${page}?${query}`
}

// The page the browser is showing, as a return path.
export function currentPath(): string {
  return window.location.pathname + window.location.search
}
