import { useRef, useState } from 'react'

type PersianDateTimeFieldProps = {
  name: string
  defaultValue?: string
  label: string
}

function formatPersianDateTime(value: string) {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(value)
  if (!match) return ''
  const [, year, month, day, hour, minute] = match
  const selectedDate = new Date(Date.UTC(Number(year), Number(month) - 1, Number(day), Number(hour), Number(minute)))
  return new Intl.DateTimeFormat('fa-IR-u-ca-persian-nu-arabext', {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone: 'UTC',
  }).format(selectedDate)
}

export function PersianDateTimeField({ name, defaultValue = '', label }: PersianDateTimeFieldProps) {
  const [value, setValue] = useState(defaultValue)
  const picker = useRef<HTMLInputElement>(null)
  const openPicker = () => {
    try {
      picker.current?.showPicker()
    } catch {
      picker.current?.click()
    }
  }

  return <div className="persian-datetime-field">
    <input
      type="text"
      readOnly
      aria-label={label}
      value={formatPersianDateTime(value)}
      placeholder="انتخاب تاریخ و ساعت"
      onClick={openPicker}
    />
    <button type="button" aria-label="انتخاب تاریخ و ساعت" onClick={openPicker}>
      <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3.5" y="5" width="17" height="15" rx="2" /><path d="M7.5 3.5v3M16.5 3.5v3M4 9.5h16M8 13h2M14 13h2M8 16h2" /></svg>
    </button>
    <input
      ref={picker}
      className="persian-datetime-picker"
      type="datetime-local"
      aria-label={label}
      tabIndex={-1}
      name={name}
      value={value}
      onChange={event => setValue(event.currentTarget.value)}
    />
  </div>
}
