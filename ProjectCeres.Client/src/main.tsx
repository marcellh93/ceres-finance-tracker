import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import { Navbar } from './components/ui/navbar'

const navbarEl = document.getElementById('navbar-root')
if (navbarEl) {
  createRoot(navbarEl).render(
    <StrictMode>
      <Navbar />
    </StrictMode>,
  )
}
