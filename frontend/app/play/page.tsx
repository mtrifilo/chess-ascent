import { PageHeading } from '@/components/layout/page-heading'

// Rendered only in the browser, for a signed-in visitor (see layout.tsx and
// RequireAccount).
export const instant = false

export default function PlayPage() {
  return <PageHeading>Play</PageHeading>
}
