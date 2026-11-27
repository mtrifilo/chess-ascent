import type { Metadata } from 'next'
import { Golos_Text, Hanken_Grotesk, Schibsted_Grotesk } from 'next/font/google'

import { AppShell } from '@/components/layout/app-shell'

import './globals.css'

// next/font downloads these from Google Fonts at build time and serves them
// from this app, so browsers never contact Google.
//
// The variable classes go on <html>, not <body>: Tailwind's preflight sets
// the page font on html from the theme's `--font-sans`, which globals.css
// maps to these variables, and a variable defined only on body is undefined
// at html. All three are variable fonts, so no weight list. `subsets` only
// chooses which files are preloaded; every subset is still served through
// unicode-range.
const hankenGrotesk = Hanken_Grotesk({
  subsets: ['latin', 'latin-ext'],
  variable: '--font-hanken',
  display: 'swap',
})

const schibstedGrotesk = Schibsted_Grotesk({
  subsets: ['latin', 'latin-ext'],
  variable: '--font-schibsted',
  display: 'swap',
})

const golosText = Golos_Text({
  subsets: ['latin', 'latin-ext'],
  variable: '--font-golos',
  display: 'swap',
})

export const metadata: Metadata = {
  title: 'Chess Ascent',
}

export default function RootLayout({ children }: LayoutProps<'/'>) {
  return (
    <html
      lang="en"
      className={`${hankenGrotesk.variable} ${schibstedGrotesk.variable} ${golosText.variable}`}
    >
      <body className="antialiased">
        {/* Signed out until the app has sessions: no email is passed. */}
        <AppShell>{children}</AppShell>
      </body>
    </html>
  )
}
