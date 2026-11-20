import { useEffect, useState } from 'react'

/**
 * The value, once it has stopped changing for `delayMs`. Typing "01712345678" fires one
 * lookup when the user pauses, not one per keystroke.
 */
export function useDebouncedValue<T>(value: T, delayMs = 300): T {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const timer = setTimeout(() => {
      setDebounced(value)
    }, delayMs)
    return () => {
      clearTimeout(timer)
    }
  }, [value, delayMs])
  return debounced
}
