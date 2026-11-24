// Mirrors the API's RolePermissions matrix for showing or hiding actions. The API still decides
// (403 for anyone else), so this is a convenience, never a security check.
const enterOrders = new Set<string>(['Owner', 'Manager', 'Moderator'])
const manageShop = new Set<string>(['Owner', 'Manager'])
const pack = new Set<string>(['Owner', 'Manager', 'Packer'])
const seeProfit = new Set<string>(['Owner'])

const has = (roles: ReadonlySet<string>, role: string | undefined): boolean =>
  role !== undefined && roles.has(role)

export const canEnterOrders = (role: string | undefined): boolean => has(enterOrders, role)
/** Shop settings, including courier connections. */
export const canManageShop = (role: string | undefined): boolean => has(manageShop, role)
/** Book couriers and print labels. */
export const canPack = (role: string | undefined): boolean => has(pack, role)
/** Money: COD, payouts, profit, exports. Hiding the screens is UX; the API returns 403 regardless. */
export const canSeeProfit = (role: string | undefined): boolean => has(seeProfit, role)
