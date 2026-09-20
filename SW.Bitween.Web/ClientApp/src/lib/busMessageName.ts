/**
 * The one rule for a bus message name, and the one way of saying it.
 *
 * Two fields name the same thing — an information type's `busMessageTypeName` and an
 * subscription's `responseMessageTypeName` — and they had drifted: one silently deleted
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

/**
 * A work group's real queue name, as `WorkGroup.GetBusMessageName()` builds it server-side:
 * the id, then the bus message name.
 *
 * It is the only thing about a group that is guaranteed to be unique. Nothing stops two
 * groups sharing both a name and a bus message name — local data has two called `test`
 * whose bus name is also `test` — and the id is what actually tells such a pair apart.
 */
export const workGroupQueueName = (group: { id: number; busMessageName: string }): string =>
  `${group.id}${group.busMessageName}`;
