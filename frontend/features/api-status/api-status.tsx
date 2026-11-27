'use client'

import { useEffect, useState } from 'react'

import { api, type ReadinessStatus } from '@/lib/api'

// The API's readiness and version, fetched from the browser through the
// typed client. A value stays blank until the API has answered with it.
export function ApiStatus() {
  const [readiness, setReadiness] = useState<ReadinessStatus>()
  const [version, setVersion] = useState<string>()

  useEffect(() => {
    // Set when the component unmounts, so a late answer is dropped.
    let unmounted = false

    fetchReadiness().then(status => {
      if (!unmounted) setReadiness(status)
    })
    fetchVersion().then(value => {
      if (!unmounted) setVersion(value)
    })

    return () => {
      unmounted = true
    }
  }, [])

  return (
    <dl className="mt-4 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
      <dt className="text-muted-foreground">API</dt>
      <dd className="font-data">{readiness}</dd>
      <dt className="text-muted-foreground">Version</dt>
      <dd className="font-data">{version}</dd>
    </dl>
  )
}

// The readiness endpoint answers in plain text, so the body is read as text,
// which openapi-fetch types as any string. It is narrowed to the documented
// values only when the status is one the document lists; any other answer,
// such as the development server's 500 when the API is not running, is not
// the API's readiness and leaves the value blank.
async function fetchReadiness(): Promise<ReadinessStatus | undefined> {
  try {
    const { data, error, response } = await api.GET('/health/ready', {
      parseAs: 'text',
    })
    if (response.status === 200) return data as ReadinessStatus
    if (response.status === 503) return error
    return undefined
  } catch {
    return undefined
  }
}

async function fetchVersion(): Promise<string | undefined> {
  try {
    const { data } = await api.GET('/version')
    return data?.version
  } catch {
    return undefined
  }
}
