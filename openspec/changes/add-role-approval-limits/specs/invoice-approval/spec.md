# Spec Delta

## Purpose

Defines who may approve a pending invoice and with what outcome, based on the approver's role, inclusive amount limits, a two-approver rule for the largest invoices, and the guarantee that a refused approval leaves the invoice unchanged.

## ADDED Requirements

### Requirement: Manager approval limit
The system SHALL allow a user with the Manager role to approve a pending invoice on their own when its amount is less than or equal to 50,000.00, making the invoice Approved. The limit SHALL be inclusive and compared as an exact decimal value. A Manager approval of a pending invoice above 50,000.00 and up to 500,000.00 SHALL be refused.

#### Scenario: Manager approves an invoice exactly at the limit
- **WHEN** a Manager approves a pending invoice of 50,000.00
- **THEN** the invoice becomes Approved
- **AND** the decision records the Manager as the approver and the time of approval

#### Scenario: Manager approves an invoice below the limit
- **WHEN** a Manager approves a pending invoice of 1,200.00
- **THEN** the invoice becomes Approved

#### Scenario: Manager is refused just over the limit
- **WHEN** a Manager approves a pending invoice of 50,000.01
- **THEN** the attempt is refused with a reason that names the Manager role, the 50,000 limit, and that a Director approval is needed
- **AND** the invoice remains PendingApproval with no recorded approval

### Requirement: Director approval limit
The system SHALL allow a user with the Director role to approve a pending invoice on their own when its amount is less than or equal to 500,000.00, making the invoice Approved. The limit SHALL be inclusive and compared as an exact decimal value.

#### Scenario: Director approves an invoice just over the Manager limit
- **WHEN** a Director approves a pending invoice of 50,000.01
- **THEN** the invoice becomes Approved

#### Scenario: Director approves an invoice exactly at the limit
- **WHEN** a Director approves a pending invoice of 500,000.00
- **THEN** the invoice becomes Approved
- **AND** the decision records the Director as the approver

### Requirement: Two distinct approvers above 500,000
The system SHALL require two approvals for a pending invoice whose amount is greater than 500,000.00. The first approval MAY come from a Manager or a Director and SHALL leave the invoice PendingApproval with one recorded approval. The second approval SHALL come from a Director and SHALL make the invoice Approved, recording the second approver as the decider.

#### Scenario: First approval just over the limit keeps the invoice pending
- **WHEN** a Director approves a pending invoice of 500,000.01 that has no recorded approvals
- **THEN** the invoice remains PendingApproval
- **AND** it has exactly one recorded approval naming that Director, their role and the time
- **AND** no decider or decision time is set

#### Scenario: Manager gives the first of two approvals
- **WHEN** a Manager approves a pending invoice of 750,000.00 that has no recorded approvals
- **THEN** the invoice remains PendingApproval with one recorded approval naming that Manager

#### Scenario: Second Director approval completes the invoice
- **WHEN** demo.director2 (Director) approves a pending invoice of 750,000.00 whose only recorded approval is by demo.manager
- **THEN** the invoice becomes Approved
- **AND** it has two recorded approvals
- **AND** the decision records demo.director2 as the approver

#### Scenario: Manager is refused as the second approver
- **WHEN** a second Manager approves a pending invoice of 750,000.00 whose only recorded approval is by demo.director
- **THEN** the attempt is refused with a reason that names the Manager role and that the second approval above 500,000 must come from a Director
- **AND** the invoice is unchanged

### Requirement: Same user cannot be both approvers
The system SHALL refuse an approval of an invoice by a user who already has a recorded approval on that invoice. Usernames SHALL be compared after trimming surrounding whitespace and ignoring letter case.

#### Scenario: Same Director approves twice
- **WHEN** demo.director approves a pending invoice of 750,000.00 whose only recorded approval is by demo.director
- **THEN** the attempt is refused with a reason stating that the second approval must come from a different Director
- **AND** the invoice remains PendingApproval with exactly one recorded approval and no decider

#### Scenario: Same user with different casing and whitespace
- **WHEN** " Demo.Director " approves a pending invoice of 750,000.00 whose only recorded approval is by demo.director
- **THEN** the attempt is refused as the same user
- **AND** the invoice is unchanged

### Requirement: Roles without an approval limit cannot approve
The system SHALL refuse approval attempts from any role that has no approval limit, including Admin and unknown roles. The system SHALL continue to allow Manager, Director and Admin to decline a pending invoice with a reason.

#### Scenario: Admin is refused
- **WHEN** an Admin attempts to approve a pending invoice of 100.00
- **THEN** the attempt is refused and the invoice is unchanged

#### Scenario: Admin can still decline
- **WHEN** an Admin declines a pending invoice with the reason "Duplicate"
- **THEN** the invoice becomes Rejected with that reason

### Requirement: Refused approval returns a clear reason
The system SHALL respond to a refused approval attempt with HTTP 403 and a JSON body `{ "error": "<reason>" }`. The reason SHALL name the approver's role, that role's limit (when it has one), and what approval is needed instead. The web UI SHALL show this reason in the decision error alert.

#### Scenario: API returns 403 with the reason
- **WHEN** demo.manager calls the approve endpoint for a pending invoice of 60,000.00
- **THEN** the response status is 403
- **AND** the body's error text contains "Manager" and "50,000"

#### Scenario: Reason shown in the UI
- **WHEN** demo.manager clicks Approve on a pending invoice of 60,000.00 in the Approvals list
- **THEN** the alert shows the refusal reason
- **AND** the invoice is still listed as pending

### Requirement: Refused approval changes nothing
The system SHALL leave every field of an invoice unchanged when an approval attempt is refused, including status, decider, decision time, decision note and recorded approvals, and SHALL NOT persist any update for that invoice.

#### Scenario: Over-limit attempt leaves the invoice untouched
- **WHEN** a Manager's approval of a pending invoice of 50,000.01 is refused
- **THEN** the invoice's status, decider, decision time, decision note and recorded approvals equal their values before the attempt
- **AND** no update is saved for the invoice

#### Scenario: Refused second approval leaves the first approval intact
- **WHEN** a same-user second approval of a pending invoice of 500,000.01 is refused
- **THEN** the invoice still has exactly the one original recorded approval and remains PendingApproval
- **AND** no update is saved for the invoice

### Requirement: Approval progress is visible
The system SHALL expose each invoice's recorded approvals, and the web UI SHALL label a pending invoice that has one of two required approvals as "1 of 2 approvals".

#### Scenario: Pending invoice awaiting its second approval
- **WHEN** an approver views the Approvals list and a pending invoice of 750,000.00 has one recorded approval
- **THEN** that invoice's entry shows "1 of 2 approvals"

#### Scenario: Single-approval invoice has no progress label
- **WHEN** an approver views the Approvals list and a pending invoice of 10,000.00 has no recorded approvals
- **THEN** that invoice's entry does not show an approval progress label
