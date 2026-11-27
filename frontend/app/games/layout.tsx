import { RequireAccount } from '@/features/auth/require-account'

// Covers /games and every page under it.
export default function GamesLayout({ children }: LayoutProps<'/games'>) {
  return <RequireAccount>{children}</RequireAccount>
}
