import { render, screen } from '@testing-library/react'
import { HelloWorld } from './HelloWorld'

test('renders welcome message', () => {
  render(<HelloWorld />)
  expect(screen.getByText('Project Ceres')).toBeInTheDocument()
})
