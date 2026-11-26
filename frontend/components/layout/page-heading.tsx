// A page's top-level heading, in the design's Display/M style (the My games
// screen's heading).
export function PageHeading({ children }: { children: React.ReactNode }) {
  return (
    <h1 className="font-display text-display-m font-semibold">{children}</h1>
  )
}
