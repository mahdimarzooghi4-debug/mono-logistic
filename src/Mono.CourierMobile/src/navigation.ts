export function getNavigationUrl(destinationAddress: string, userAgent = typeof navigator === 'undefined' ? '' : navigator.userAgent) {
  const destination = encodeURIComponent(destinationAddress)

  if (/android/i.test(userAgent)) {
    return `geo:0,0?q=${destination}`
  }

  if (/iPhone|iPad|iPod/i.test(userAgent)) {
    return `https://maps.apple.com/?daddr=${destination}&dirflg=d`
  }

  return `https://www.google.com/maps/dir/?api=1&destination=${destination}&travelmode=driving`
}
