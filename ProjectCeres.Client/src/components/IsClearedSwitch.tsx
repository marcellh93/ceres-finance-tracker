import { useState } from 'react'
import { Switch } from '@/components/ui/switch'

interface IsClearedSwitchProps {
  fieldName: string
  initialChecked: boolean
}

export function IsClearedSwitch({ fieldName, initialChecked }: IsClearedSwitchProps) {
  const [checked, setChecked] = useState(initialChecked)

  return (
    <div className="flex items-center gap-3">
      <Switch checked={checked} onCheckedChange={setChecked} aria-label="Cleared" />
      <span className="text-sm text-foreground">{checked ? 'Cleared' : 'Uncleared'}</span>
      <input type="hidden" name={fieldName} value={checked ? 'true' : 'false'} />
    </div>
  )
}
