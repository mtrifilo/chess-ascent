import { RequireAccount } from '@/features/auth/require-account'

export default function PlayLayout({ children }: LayoutProps<'/play'>) {
  return <RequireAccount>{children}</RequireAccount>
}
