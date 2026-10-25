CREATE TABLE public_registration_requests (
    request_id text PRIMARY KEY CHECK (request_id ~ '^reg_[0-9a-f]{32}$'),
    request_type text NOT NULL CHECK (request_type IN ('BUSINESS', 'COURIER')),
    encrypted_payload text NOT NULL,
    status text NOT NULL DEFAULT 'SUBMITTED'
        CHECK (status IN ('SUBMITTED', 'CONTACT_ATTEMPTED', 'DECLINED', 'CLOSED')),
    privacy_notice_version text NOT NULL,
    consented_at timestamptz NOT NULL,
    idempotency_digest text NOT NULL UNIQUE,
    request_digest text NOT NULL,
    submitted_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,
    CHECK (expires_at <= submitted_at + interval '90 days')
);
CREATE INDEX public_registration_requests_queue_idx
    ON public_registration_requests (submitted_at DESC, request_id)
    WHERE status IN ('SUBMITTED', 'CONTACT_ATTEMPTED');
CREATE INDEX public_registration_requests_expiry_idx ON public_registration_requests (expires_at);

CREATE TABLE public_registration_request_events (
    event_id bigserial PRIMARY KEY,
    request_id text NOT NULL REFERENCES public_registration_requests(request_id) ON DELETE CASCADE,
    actor_id text NOT NULL,
    from_status text NOT NULL,
    to_status text NOT NULL,
    reason_code text,
    created_at timestamptz NOT NULL
);
CREATE INDEX public_registration_request_events_request_idx
    ON public_registration_request_events (request_id, created_at);
CREATE TABLE public_registration_request_access_audit (
    audit_id bigserial PRIMARY KEY,
    actor_id text NOT NULL,
    request_count integer NOT NULL CHECK (request_count >= 0),
    created_at timestamptz NOT NULL
);
CREATE INDEX public_registration_request_access_audit_created_idx
    ON public_registration_request_access_audit (created_at DESC);
