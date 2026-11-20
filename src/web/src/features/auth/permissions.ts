// Mirrors the API's RolePermissions matrix for showing or hiding actions. The API still decides
// (403 for anyone else), so this is a convenience, never a security check.
const enterOrders = new Set<string>(['Owner', 'Manager', 'Moderator'])

export const canEnterOrders = (role: string | undefined): boolean =>
  role !== undefined && enterOrders.has(role)
