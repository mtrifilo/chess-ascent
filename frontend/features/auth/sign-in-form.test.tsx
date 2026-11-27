import { screen, waitFor } from '@testing-library/react'
import { useRouter, useSearchParams } from 'next/navigation'
import { Activity } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import {
  answer,
  fillIn,
  renderWithQueries,
  routerWith,
  SIGNED_OUT,
} from '@/lib/test-support'

import { SignInForm } from './sign-in-form'

vi.mock('next/navigation', () => ({
  useRouter: vi.fn(),
  useSearchParams: vi.fn(),
}))

const replace = vi.fn()

function openWith(query = '') {
  vi.mocked(useSearchParams).mockReturnValue(
    new URLSearchParams(query) as ReturnType<typeof useSearchParams>
  )
}

beforeEach(() => {
  replace.mockReset()
  vi.mocked(useRouter).mockReturnValue(routerWith(replace))
  openWith()
})

describe('SignInForm', () => {
  it('shows the fields, button and link as drawn', () => {
    answer({ '/api/auth/me': SIGNED_OUT })
    renderWithQueries(<SignInForm />)

    expect(
      screen.getByRole('heading', { level: 1, name: 'Sign in' })
    ).toBeInTheDocument()
    const email = screen.getByLabelText('Email')
    expect(email).toHaveAttribute('type', 'email')
    expect(email).toHaveAttribute('autocomplete', 'username')
    expect(email).toHaveAttribute('placeholder', 'you@example.com')
    expect(email).toBeRequired()
    const password = screen.getByLabelText('Password')
    expect(password).toHaveAttribute('type', 'password')
    expect(password).toHaveAttribute('autocomplete', 'current-password')
    expect(password).toBeRequired()
    expect(screen.getByText('No account?')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Sign up' })).toHaveAttribute(
      'href',
      '/sign-up'
    )
  })

  it('posts rather than putting the fields in the address if submitted natively', () => {
    answer({ '/api/auth/me': SIGNED_OUT })
    renderWithQueries(<SignInForm />)

    expect(
      screen.getByRole('button', { name: 'Sign in' }).closest('form')
    ).toHaveAttribute('method', 'post')
  })

  it('sends the trimmed email and the password, then goes to /play', async () => {
    const sent = answer({
      '/api/auth/me': SIGNED_OUT,
      '/api/auth/sign-in': {
        status: 200,
        body: { email: 'player@example.com' },
      },
    })
    renderWithQueries(<SignInForm />)

    fillIn('  player@example.com ', ' secret ', 'Sign in')

    await waitFor(() => expect(replace).toHaveBeenCalledWith('/play'))
    expect(sent).toContainEqual({
      call: 'POST /api/auth/sign-in',
      body: { email: 'player@example.com', password: ' secret ' },
    })
  })

  it('returns to the page the visitor was sent from', async () => {
    openWith('next=%2Fgames%2F42')
    answer({
      '/api/auth/me': SIGNED_OUT,
      '/api/auth/sign-in': { status: 200, body: { email: 'a@example.com' } },
    })
    renderWithQueries(<SignInForm />)

    fillIn('a@example.com', 'secret', 'Sign in')

    await waitFor(() => expect(replace).toHaveBeenCalledWith('/games/42'))
  })

  it('goes to /play, not another site, when the return address points elsewhere', async () => {
    openWith('next=https%3A%2F%2Fevil.example%2Fphish')
    answer({
      '/api/auth/me': SIGNED_OUT,
      '/api/auth/sign-in': { status: 200, body: { email: 'a@example.com' } },
    })
    renderWithQueries(<SignInForm />)

    fillIn('a@example.com', 'secret', 'Sign in')

    await waitFor(() => expect(replace).toHaveBeenCalledWith('/play'))
    expect(replace).not.toHaveBeenCalledWith(
      expect.stringContaining('evil.example')
    )
  })

  it('carries the return address to the sign-up link', () => {
    openWith('next=%2Fgames')
    answer({ '/api/auth/me': SIGNED_OUT })
    renderWithQueries(<SignInForm />)

    expect(screen.getByRole('link', { name: 'Sign up' })).toHaveAttribute(
      'href',
      '/sign-up?next=%2Fgames'
    )
  })

  it('sends a visitor who is already signed in on to where they were headed', async () => {
    openWith('next=%2Fgames')
    answer({
      '/api/auth/me': { status: 200, body: { email: 'a@example.com' } },
    })
    renderWithQueries(<SignInForm />)

    await waitFor(() => expect(replace).toHaveBeenCalledWith('/games'))
  })

  it('shows one message for a wrong email or password, naming neither', async () => {
    answer({
      '/api/auth/me': SIGNED_OUT,
      '/api/auth/sign-in': {
        status: 401,
        body: { status: 401, detail: 'Wording the page does not show.' },
      },
    })
    renderWithQueries(<SignInForm />)

    fillIn('player@example.com', 'wrong', 'Sign in')

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(
      'Email or password is incorrect.Check both and try again.'
    )
    expect(screen.getByLabelText('Email')).not.toHaveAttribute('aria-invalid')
    expect(screen.getByLabelText('Password')).not.toHaveAttribute(
      'aria-invalid'
    )
    expect(screen.getByLabelText('Password')).toHaveValue('wrong')
    expect(replace).not.toHaveBeenCalled()
  })

  it('clears the fields and the last answer when Next.js hides the page', async () => {
    answer({
      '/api/auth/me': SIGNED_OUT,
      '/api/auth/sign-in': { status: 401, body: { status: 401 } },
    })
    const { rerender } = renderWithQueries(
      <Activity mode="visible">
        <SignInForm />
      </Activity>
    )
    fillIn('player@example.com', 'wrong', 'Sign in')
    await screen.findByRole('alert')

    rerender(
      <Activity mode="hidden">
        <SignInForm />
      </Activity>
    )
    rerender(
      <Activity mode="visible">
        <SignInForm />
      </Activity>
    )

    expect(screen.getByLabelText('Password')).toHaveValue('')
    expect(screen.getByLabelText('Email')).toHaveValue('')
    await waitFor(() =>
      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    )
  })

  it('shows the errors the API reports on each field', async () => {
    answer({
      '/api/auth/me': SIGNED_OUT,
      '/api/auth/sign-in': {
        status: 400,
        body: {
          status: 400,
          errors: { Email: ['The Email field is required.'] },
        },
      },
    })
    renderWithQueries(<SignInForm />)

    fillIn('x@example.com', 'secret', 'Sign in')

    const email = screen.getByLabelText('Email')
    await waitFor(() => expect(email).toHaveAttribute('aria-invalid', 'true'))
    expect(email).toHaveAccessibleDescription('The Email field is required.')
    expect(email).toHaveFocus()
  })
})
