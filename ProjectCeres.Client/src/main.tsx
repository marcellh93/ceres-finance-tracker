import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import { HelloWorld } from './components/HelloWorld'
import { Navbar } from './components/ui/navbar'

const navbarEl = document.getElementById('navbar-root')
if (navbarEl) {
  createRoot(navbarEl).render(
    <StrictMode>
      <Navbar />
    </StrictMode>,
  )
}

const rootEl = document.getElementById('react-root')
if (rootEl) {
  createRoot(rootEl).render(
    <StrictMode>
      <HelloWorld />
    </StrictMode>,
  )
}
