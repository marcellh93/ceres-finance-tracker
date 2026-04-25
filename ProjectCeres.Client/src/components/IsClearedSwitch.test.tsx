import { render, fireEvent } from '@testing-library/react'
import { IsClearedSwitch } from './IsClearedSwitch'

test('renders unchecked initially when initialChecked is false', () => {
  render(<IsClearedSwitch fieldName="IsCleared" initialChecked={false} />)
  const root = document.querySelector('[data-slot="switch"]')
  expect(root).not.toBeNull()
  expect(root?.getAttribute('data-checked')).toBeNull()
  expect(root?.getAttribute('data-unchecked')).toBe('')
})

test('renders checked initially when initialChecked is true', () => {
  render(<IsClearedSwitch fieldName="IsCleared" initialChecked={true} />)
  const root = document.querySelector('[data-slot="switch"]')
  expect(root?.getAttribute('data-checked')).toBe('')
  expect(root?.getAttribute('data-unchecked')).toBeNull()
})

test('toggling updates data attributes and hidden input', () => {
  render(<IsClearedSwitch fieldName="IsCleared" initialChecked={false} />)
  const root = document.querySelector('[data-slot="switch"]') as HTMLElement
  expect(root.getAttribute('data-unchecked')).toBe('')

  fireEvent.click(root)

  expect(root.getAttribute('data-checked')).toBe('')
  expect(root.getAttribute('data-unchecked')).toBeNull()

  const hidden = document.querySelector('input[type="hidden"]') as HTMLInputElement
  expect(hidden.value).toBe('true')
})

test('thumb has data-checked when switch is checked', () => {
  render(<IsClearedSwitch fieldName="IsCleared" initialChecked={false} />)
  const root = document.querySelector('[data-slot="switch"]') as HTMLElement

  fireEvent.click(root)

  const thumb = document.querySelector('[data-slot="switch-thumb"]')
  expect(thumb?.getAttribute('data-checked')).toBe('')
  expect(thumb?.getAttribute('data-unchecked')).toBeNull()
})

test('root has group/switch class', () => {
  render(<IsClearedSwitch fieldName="IsCleared" initialChecked={false} />)
  const root = document.querySelector('[data-slot="switch"]')
  expect(root?.classList.contains('group/switch')).toBe(true)
})

test('root has data-size="default"', () => {
  render(<IsClearedSwitch fieldName="IsCleared" initialChecked={false} />)
  const root = document.querySelector('[data-slot="switch"]')
  expect(root?.getAttribute('data-size')).toBe('default')
})

test('diagnostic: thumb class and attributes after toggle', () => {
  render(<IsClearedSwitch fieldName="IsCleared" initialChecked={false} />)
  const root = document.querySelector('[data-slot="switch"]') as HTMLElement
  fireEvent.click(root)
  const thumb = document.querySelector('[data-slot="switch-thumb"]') as HTMLElement
  console.log('THUMB CLASS:', thumb?.className)
  console.log('THUMB data-checked:', thumb?.getAttribute('data-checked'))
  console.log('THUMB data-unchecked:', thumb?.getAttribute('data-unchecked'))
  console.log('THUMB outerHTML:', thumb?.outerHTML)
  expect(true).toBe(true)
})
