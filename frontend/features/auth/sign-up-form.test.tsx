import { screen, waitFor, within } from '@testing-library/react'
import { useRouter, useSearchParams } from 'next/navigation'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import {
  answer,
  fillIn,
  renderWithQueries,
  routerWith,
  SIGNED_OUT,
} from '@/lib/test-support'

import { SignUpForm } from './sign-up-form'

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

// The errors under a field, in order.
function errorLines(field: HTMLElement) {
  const id = field.getAttribute('aria-describedby')
  const list = id ? document.getElementById(id) : null
  return list
    ? within(list)
        .getAllByRole('listitem')
        .map(item => item.textContent)
    : []
}

describe('SignUpForm', () => {
  it('shows the fields, button and link as drawn', () => {
    answer({ '/api/auth/me': SIGNED_OUT })
    renderWithQueries(<SignUpForm />)

    expect(
      screen.getByRole('heading', { level: 1, name: 'Sign up' })
    ).toBeInTheDocument()
    const email = screen.getByLabelText('Email')
    expect(email).toHaveAttribute('type', 'email')
    expect(email).toHaveAttribute('autocomplete', 'username')
    expect(email).toHaveAttribute('placeholder', 'you@example.com')
    expect(email).toBeRequired()
    const password = screen.getByLabelText('Password')
    expect(password).toHaveAttribute('type', 'password')
    expect(password).toHaveAttribute('autocomplete', 'new-password')
    expect(password).toBeRequired()
    expect(
      screen.getByRole('button', { name: 'Sign up' }).closest('form')
    ).toHaveAttribute('method', 'post')
    expect(screen.getByText('Have an account?')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Sign in' })).toHaveAttribute(
      'href',
      '/sign-in'
    )
  })

  it('registers with the trimmed email and lands signed in on /play', async () => {
    const sent = answer({
      '/api/auth/me': SIGNED_OUT,
      '/api/auth/register': {
        status: 200,
        body: { email: 'new@example.com' },
      },
    })
    renderWithQueries(<SignUpForm />)

    fillIn(' new@example.com  ', 'Secret-1', 'Sign up')

    await waitFor(() => expect(replace).toHaveBeenCalledWith('/play'))
    expect(sent).toContainEqual({
      call: 'POST /api/auth/register',
      body: { email: 'new@example.com', password: 'Secret-1' },
    })
  })

  it('returns to the page the visitor was sent from, carried over from sign-in', async () => {
    openWith('next=%2Fgames')
    answer({
      '/api/auth/me': SIGNED_OUT,
      '/api/auth/register': { status: 200, body: { email: 'a@example.com' } },
    })
    renderWithQueries(<SignUpForm />)

    expect(screen.getByRole('link', { name: 'Sign in' })).toHaveAttribute(
      'href',
      '/sign-in?next=%2Fgames'
    )
    fillIn('a@example.com', 'Secret-1', 'Sign up')

    await waitFor(() => expect(replace).toHaveBeenCalledWith('/games'))
  })

  it('shows the duplicate-email message on the email field', async () => {
    answer({
      '/api/auth/me': SIGNED_OUT,
      '/api/auth/register': {
        status: 409,
        body: {
          status: 409,
          detail: 'Wording the page does not show.',
        },
      },
    })
    renderWithQueries(<SignUpForm />)

    fillIn('player@example.com', 'Secret-1', 'Sign up')

    const email = screen.getByLabelText('Email')
    await waitFor(() => expect(email).toHaveAttribute('aria-invalid', 'true'))
    expect(errorLines(email)).toEqual([
      'An account with this email already exists.',
    ])
    expect(email).toHaveFocus()
    expect(screen.getByLabelText('Password')).not.toHaveAttribute(
      'aria-invalid'
    )
    expect(replace).not.toHaveBeenCalled()
  })

  it('shows the approved line, then each rule the API says the password breaks', async () => {
    const rules = [
      "Passwords must have at least one digit ('0'-'9').",
      "Passwords must have at least one uppercase ('A'-'Z').",
    ]
    answer({
      '/api/auth/me': SIGNED_OUT,
      '/api/auth/register': {
        status: 400,
        body: { status: 400, errors: { Password: rules } },
      },
    })
    renderWithQueries(<SignUpForm />)

    fillIn('player@example.com', 'secret', 'Sign up')

    const password = screen.getByLabelText('Password')
    await waitFor(() =>
      expect(password).toHaveAttribute('aria-invalid', 'true')
    )
    expect(errorLines(password)).toEqual([
      'This password does not meet the requirements.',
      ...rules,
    ])
    expect(password).toHaveFocus()
    expect(screen.getByLabelText('Email')).not.toHaveAttribute('aria-invalid')
  })

  it('shows an email the API refuses with the API’s own words', async () => {
    answer({
      '/api/auth/me': SIGNED_OUT,
      '/api/auth/register': {
        status: 400,
        body: {
          status: 400,
          errors: { Email: ["Email 'o'neill@example.com' is invalid."] },
        },
      },
    })
    renderWithQueries(<SignUpForm />)

    fillIn("o'neill@example.com", 'Secret-1', 'Sign up')

    const email = screen.getByLabelText('Email')
    await waitFor(() => expect(email).toHaveAttribute('aria-invalid', 'true'))
    expect(errorLines(email)).toEqual([
      "Email 'o'neill@example.com' is invalid.",
    ])
  })

  it('signs up on a second attempt after the first is refused', async () => {
    answer({
      '/api/auth/me': SIGNED_OUT,
      '/api/auth/register': { status: 409, body: { status: 409 } },
    })
    renderWithQueries(<SignUpForm />)
    fillIn('player@example.com', 'Secret-1', 'Sign up')
    await waitFor(() =>
      expect(screen.getByLabelText('Email')).toHaveAttribute('aria-invalid')
    )

    answer({
      '/api/auth/me': SIGNED_OUT,
      '/api/auth/register': { status: 200, body: { email: 'b@example.com' } },
    })
    fillIn('b@example.com', 'Secret-1', 'Sign up')

    await waitFor(() => expect(replace).toHaveBeenCalledWith('/play'))
  })
})
