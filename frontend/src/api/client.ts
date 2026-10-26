import type {
  AssistantAnswer,
  LlmStatus,
  Container,
  ContainerDetail,
  ContainerFields,
  Part,
  PartFields,
  Project,
  RecognitionResponse,
  ShoppingListFields,
  ShoppingListItem,
  SuggestedPart,
} from './types'

export class ApiError extends Error {
  readonly status: number

  constructor(message: string, status: number) {
    super(message)
    this.status = status
  }
}

interface ProblemDetails {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const isForm = init.body instanceof FormData
  const response = await fetch(`/api${path}`, {
    ...init,
    headers: isForm || init.body === undefined ? init.headers : { 'Content-Type': 'application/json', ...init.headers },
  })

  if (!response.ok) {
    throw new ApiError(await errorMessage(response), response.status)
  }
  if (response.status === 204) {
    return undefined as T
  }
  return (await response.json()) as T
}

async function errorMessage(response: Response): Promise<string> {
  const text = await response.text()
  try {
    const problem = JSON.parse(text) as ProblemDetails | string
    if (typeof problem === 'string') return problem
    const validation = problem.errors ? Object.values(problem.errors).flat().join(' ') : ''
    return validation || problem.detail || problem.title || response.statusText
  } catch {
    return text || response.statusText
  }
}

const json = (body: unknown) => JSON.stringify(body)

export const api = {
  parts: {
    list: (q?: string) => request<Part[]>(`/parts${q ? `?q=${encodeURIComponent(q)}` : ''}`),
    get: (id: string) => request<Part>(`/parts/${id}`),
    create: (part: PartFields, quantity: number, containerId: string | null, imageId: string | null) =>
      request<Part>('/parts', { method: 'POST', body: json({ part, quantity, containerId, imageId }) }),
    update: (id: string, part: PartFields) => request<Part>(`/parts/${id}`, { method: 'PUT', body: json(part) }),
    remove: (id: string) => request<void>(`/parts/${id}`, { method: 'DELETE' }),
    setStock: (id: string, containerId: string | null, quantity: number) =>
      request<Part>(`/parts/${id}/stock`, { method: 'PUT', body: json({ containerId, quantity }) }),
  },
  images: {
    url: (id: string) => `/api/images/${id}`,
    assign: (id: string, partId: string) => request<void>(`/images/${id}/assign/${partId}`, { method: 'POST' }),
  },
  recognition: {
    recognize: (file: Blob, hint?: string) => {
      const form = new FormData()
      form.append('file', file)
      if (hint) form.append('hint', hint)
      return request<RecognitionResponse>('/recognition', { method: 'POST', body: form })
    },
  },
  containers: {
    list: () => request<Container[]>('/containers'),
    get: (code: string) => request<ContainerDetail>(`/containers/${encodeURIComponent(code)}`),
    create: (fields: ContainerFields) => request<Container>('/containers', { method: 'POST', body: json(fields) }),
    update: (id: string, fields: ContainerFields) => request<Container>(`/containers/${id}`, { method: 'PUT', body: json(fields) }),
    remove: (id: string) => request<void>(`/containers/${id}`, { method: 'DELETE' }),
    labelUrl: (code: string) => `/api/containers/${encodeURIComponent(code)}/label.svg`,
  },
  llm: {
    status: () => request<LlmStatus>('/llm/status'),
  },
  assistant: {
    ask: (question: string) => request<AssistantAnswer>('/assistant/ask', { method: 'POST', body: json({ question }) }),
  },
  projects: {
    list: () => request<Project[]>('/projects'),
    save: (project: { name: string; description: string | null; question: string | null; sources: string[]; parts: SuggestedPart[] }) =>
      request<Project>('/projects', { method: 'POST', body: json(project) }),
    remove: (id: string) => request<void>(`/projects/${id}`, { method: 'DELETE' }),
    addMissingToShoppingList: (id: string) => request<ShoppingListItem[]>(`/projects/${id}/shopping-list`, { method: 'POST' }),
  },
  shoppingList: {
    list: () => request<ShoppingListItem[]>('/shopping-list'),
    create: (fields: ShoppingListFields) => request<ShoppingListItem>('/shopping-list', { method: 'POST', body: json(fields) }),
    update: (id: string, fields: ShoppingListFields) =>
      request<ShoppingListItem>(`/shopping-list/${id}`, { method: 'PUT', body: json(fields) }),
    remove: (id: string) => request<void>(`/shopping-list/${id}`, { method: 'DELETE' }),
  },
}
