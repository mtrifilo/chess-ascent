'use client'

import { useEffect, useState } from 'react'

import { api } from '@/lib/api'
import type { paths } from '@/types/api'

type Readiness = paths['/health/ready']['get']['responses']
type ReadinessCode = 200 | 503
type ReadinessStatus = Readiness[ReadinessCode]['content']['text/plain']

// The bodies the API documents for each readiness status code. The type
// checks each entry against the generated document.
const readinessBodies: {
  [Code in ReadinessCode]: readonly Readiness[Code]['content']['text/plain'][]
} = {
  200: ['Healthy', 'Degraded'],
  503: ['Unhealthy'],
}

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

// The readiness endpoint answers in plain text, which openapi-fetch types as
// any string, and on an error status it also tries to parse the body as
// JSON. So the value shown is only ever one the document lists for the status
// that came back. Any other answer, such as the development server's 500 when
// the API is not running, or a problem-details 503 from something in front of
// the API, is not the API's readiness and leaves the value blank.
async function fetchReadiness(): Promise<ReadinessStatus | undefined> {
  try {
    const { data, error, response } = await api.GET('/health/ready', {
      parseAs: 'text',
    })
    const code = response.status
    if (code !== 200 && code !== 503) return undefined
    return readinessBodies[code].find(body => body === (data ?? error))
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
