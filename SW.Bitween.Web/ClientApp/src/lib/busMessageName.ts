/**
 * The one rule for a bus message name, and the one way of saying it.
 *
 * Two fields name the same thing — an information type's `busMessageTypeName` and an
 * integration's `responseMessageTypeName` — and they had drifted: one silently deleted
 * spaces as you typed, the other refused them and said why, and only one mentioned the
 * rule at all. Whatever the rule becomes, both fields read it from here.
 */

/** Why a name is refused, or null when it is fine. */
export const busMessageNameProblem = (name: string): string | null =>
  /\s/.test(name)
    ? "A bus message name cannot contain spaces — it becomes the routing key."
    : null;

/**
 * Matches the convention every existing type follows. Deliberately not `purchase-order`:
 * that suggested kebab-case while the real names are all `ShipmentLabelIssued`, and
 * capitals are free — publisher and consumer both lower-case the routing key, so
 * `MyMessage` and `mymessage` are the same message on the wire.
 */
export const BUS_MESSAGE_NAME_PLACEHOLDER = "PurchaseOrderReceived";
