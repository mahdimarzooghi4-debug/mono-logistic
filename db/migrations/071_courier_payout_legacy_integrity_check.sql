-- Run the installed legacy payout check as a separate migration step.
SELECT assert_existing_courier_payout_ledger_integrity();
