export const partCategories = [
  'Other',
  'Microcontroller',
  'BreakoutBoard',
  'Sensor',
  'Display',
  'Actuator',
  'PowerSupply',
  'Ic',
  'Passive',
  'Semiconductor',
  'Connector',
  'Module',
] as const
export type PartCategory = (typeof partCategories)[number]

export const categoryLabels: Record<PartCategory, string> = {
  Other: 'Sonstiges',
  Microcontroller: 'Mikrocontroller',
  BreakoutBoard: 'Breakout-Board',
  Sensor: 'Sensor',
  Display: 'Display',
  Actuator: 'Aktor',
  PowerSupply: 'Stromversorgung',
  Ic: 'IC',
  Passive: 'Passives Bauteil',
  Semiconductor: 'Halbleiter',
  Connector: 'Steckverbinder',
  Module: 'Modul',
}

export const containerTypes = ['Tray', 'Drawer', 'Box', 'Bag', 'Shelf', 'Other'] as const
export type ContainerType = (typeof containerTypes)[number]

export const containerTypeLabels: Record<ContainerType, string> = {
  Tray: 'Tray',
  Drawer: 'Schublade',
  Box: 'Box',
  Bag: 'Tüte',
  Shelf: 'Regal',
  Other: 'Sonstiges',
}

export interface Stock {
  containerId: string | null
  containerCode: string | null
  quantity: number
  note: string | null
}

export interface PartFields {
  name: string
  category: PartCategory
  manufacturer: string | null
  partNumber: string | null
  package: string | null
  description: string | null
  datasheetUrl: string | null
  tags: string[]
}

export interface Part extends PartFields {
  id: string
  totalQuantity: number
  stock: Stock[]
  imageIds: string[]
}

export interface Container {
  id: string
  code: string
  name: string | null
  type: ContainerType
  description: string | null
  parentId: string | null
  parentCode: string | null
  partCount: number
}

export interface ContainerFields {
  code: string | null
  name: string | null
  type: ContainerType
  description: string | null
  parentId: string | null
}

export interface ContainerDetail {
  container: Container
  children: Container[]
  contents: { partId: string; partName: string; partNumber: string | null; quantity: number }[]
}

export interface RecognizedPart {
  name: string
  category: PartCategory
  manufacturer: string | null
  partNumber: string | null
  package: string | null
  description: string | null
  isBreakoutBoard: boolean
  markingsRead: string[]
  alternatives: string[]
  confidence: number
}

export interface RecognitionResponse {
  imageId: string
  recognized: RecognizedPart
  matches: { partId: string; name: string; partNumber: string | null; totalQuantity: number; score: number }[]
}

export interface SuggestedPart {
  name: string
  partNumber: string | null
  quantity: number
  optional: boolean
}

export interface PartAvailability {
  name: string
  partNumber: string | null
  quantityRequired: number
  optional: boolean
  matchedPartId: string | null
  matchedPartName: string | null
  quantityInStock: number
  containers: string[]
  quantityMissing: number
}

export interface ProjectProposal {
  name: string
  description: string
  difficulty: string
  sources: string[]
  parts: PartAvailability[]
}

export interface AssistantAnswer {
  question: string
  research: string
  projects: ProjectProposal[]
}

export interface Project {
  id: string
  name: string
  description: string | null
  question: string | null
  sources: string[]
  createdAt: string
  parts: PartAvailability[]
}

export interface ShoppingListItem {
  id: string
  name: string
  partNumber: string | null
  quantity: number
  url: string | null
  projectId: string | null
  projectName: string | null
  done: boolean
}

export type ShoppingListFields = Pick<ShoppingListItem, 'name' | 'partNumber' | 'quantity' | 'url' | 'done'>
