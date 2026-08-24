import { AlertTriangle, Droplets, LoaderCircle, UserRound } from 'lucide-react';
import {
  createContext,
  useContext,
  useEffect,
  useState,
} from 'react';
import type { ReactNode } from 'react';
import ThemedSelect from '../components/ThemedSelect';

export type StaffRole = 'dealerTechnician' | 'dealerAdministrator' | 'waterFlexEmployee' | 'waterFlexAdministrator';

export interface DevelopmentUser {
  userId: string;
  displayName: string;
  role: StaffRole;
  dealerExternalId: string | null;
  dealerName: string | null;
}

interface DevelopmentIdentityValue {
  users: DevelopmentUser[];
  selectedUserId: string;
  currentUser: DevelopmentUser | null;
  selectUser: (userId: string) => void;
}

type IdentityStatus = 'loading' | 'activating' | 'active' | 'unauthorized' | 'error';
type IdentityMode = 'development' | 'production';

interface StaffSession {
  status: 'active' | 'activationRequired';
  user: DevelopmentUser | null;
}

const HEADER_NAME = 'X-WaterFlex-Development-User';
const STORAGE_KEY = 'waterflex-development-user';
const DEFAULT_USER_ID = 'wf-ops-alex';
const DevelopmentIdentityContext = createContext<DevelopmentIdentityValue | null>(null);
let activationRequest: Promise<DevelopmentUser> | null = null;

export function DevelopmentIdentityProvider({
  children,
  mode = import.meta.env.DEV ? 'development' : 'production',
}: {
  children: ReactNode;
  mode?: IdentityMode;
}) {
  const [users, setUsers] = useState<DevelopmentUser[]>([]);
  const [status, setStatus] = useState<IdentityStatus>('loading');
  const [retry, setRetry] = useState(0);
  const [selectedUserId, setSelectedUserId] = useState(() =>
    window.localStorage.getItem(STORAGE_KEY) || DEFAULT_USER_ID);

  useEffect(() => {
    const controller = new AbortController();
    setStatus('loading');
    const initialize = mode === 'development'
      ? () => loadDevelopmentUsers(controller.signal)
      : async () => {
          const session = await loadStaffSession(controller.signal);
          if (session.status === 'active' && session.user) return [session.user];
          setStatus('activating');
          return [await activateStaffInvitation()];
        };

    initialize()
      .then((availableUsers) => {
        if (controller.signal.aborted) return;
        setUsers(availableUsers);
        if (availableUsers.length === 0) {
          setStatus('unauthorized');
          return;
        }
        if (!availableUsers.some((user) => user.userId === selectedUserId)) {
          const fallback = availableUsers.find((user) => user.role === 'waterFlexEmployee')
            ?? availableUsers[0];
          selectUser(fallback.userId);
        }
        setStatus('active');
      })
      .catch((reason: unknown) => {
        if (controller.signal.aborted) return;
        setUsers([]);
        setStatus(reason instanceof IdentityRequestError && [401, 403].includes(reason.status)
          ? 'unauthorized'
          : 'error');
      });
    return () => controller.abort();
  }, [mode, retry]);

  function selectUser(userId: string) {
    window.localStorage.setItem(STORAGE_KEY, userId);
    setSelectedUserId(userId);
  }

  const currentUser = users.find((user) => user.userId === selectedUserId) ?? null;
  return (
    <DevelopmentIdentityContext.Provider value={{ users, selectedUserId, currentUser, selectUser }}>
      {status === 'active' ? children : (
        <IdentityStatusPage
          status={status}
          // Unauthorized needs a real navigation, not just a re-fetch: Cloudflare Access
          // only silently renews an expired session cookie during a top-level navigation.
          onRetry={status === 'unauthorized' ? () => window.location.reload() : () => setRetry((value) => value + 1)}
        />
      )}
    </DevelopmentIdentityContext.Provider>
  );
}

class IdentityRequestError extends Error {
  constructor(public readonly status: number) {
    super(`Staff identity request failed with status ${status}.`);
  }
}

async function loadDevelopmentUsers(signal: AbortSignal): Promise<DevelopmentUser[]> {
  const response = await fetch('/api/v1/development/users', { signal });
  if (!response.ok) throw new IdentityRequestError(response.status);
  return await response.json() as DevelopmentUser[];
}

async function loadStaffSession(signal: AbortSignal): Promise<StaffSession> {
  const response = await fetch('/api/v1/staff/session', {
    signal,
    headers: { 'X-Requested-With': 'XMLHttpRequest' },
  });
  if (!response.ok) throw new IdentityRequestError(response.status);
  return await response.json() as StaffSession;
}

function activateStaffInvitation(): Promise<DevelopmentUser> {
  if (!activationRequest) {
    activationRequest = fetch('/api/v1/staff/activate', {
      method: 'POST',
      headers: {
        'X-Requested-With': 'XMLHttpRequest',
        'X-WaterFlex-Request': 'console',
      },
    }).then(async (response) => {
      if (!response.ok) throw new IdentityRequestError(response.status);
      return await response.json() as DevelopmentUser;
    }).finally(() => {
      activationRequest = null;
    });
  }
  return activationRequest;
}

function IdentityStatusPage({ status, onRetry }: { status: IdentityStatus; onRetry: () => void }) {
  const busy = status === 'loading' || status === 'activating';
  const title = status === 'activating'
    ? 'Setting up your WaterFlex access'
    : status === 'unauthorized'
      ? 'WaterFlex access has not been provisioned'
      : status === 'error'
        ? 'WaterFlex could not verify your access'
        : 'Loading WaterFlex';
  const message = status === 'activating'
    ? 'Your invitation was found. We are securely activating your assigned role.'
    : status === 'unauthorized'
      ? 'Sign in with the email address that received the invitation, or contact a WaterFlex administrator.'
      : status === 'error'
        ? 'The identity service is temporarily unavailable. Try again in a moment.'
        : 'Checking your identity and assigned role.';

  return (
    <main className="identity-state-page">
      <section className="identity-state-card" aria-live="polite">
        <span className="identity-state-mark"><Droplets size={28} /></span>
        {busy ? <LoaderCircle className="spin" size={26} /> : <AlertTriangle size={26} />}
        <h1>{title}</h1>
        <p>{message}</p>
        {!busy && <button className="primary-button" type="button" onClick={onRetry}>Try again</button>}
      </section>
    </main>
  );
}

export function useDevelopmentIdentity(): DevelopmentIdentityValue {
  const value = useContext(DevelopmentIdentityContext);
  if (!value) throw new Error('Development identity provider is missing.');
  return value;
}

export function DevelopmentIdentitySelector() {
  const { users, selectedUserId, selectUser } = useDevelopmentIdentity();
  if (!import.meta.env.DEV) return null;
  const options = users.length > 0
    ? users.map((user) => ({
        value: user.userId,
        label: `${user.displayName} · ${user.dealerName ?? 'WaterFlex'}`,
      }))
    : [{ value: selectedUserId, label: 'Loading identity' }];

  return (
    <div className="identity-selector" title="Development identity">
      <UserRound size={16} />
      <ThemedSelect
        value={selectedUserId}
        options={options}
        ariaLabel="Development identity"
        disabled={users.length === 0}
        onValueChange={selectUser}
      />
    </div>
  );
}

export function developmentIdentityHeaders(): Record<string, string> {
  if (!import.meta.env.DEV) return {};
  const userId = window.localStorage.getItem(STORAGE_KEY) || DEFAULT_USER_ID;
  return { [HEADER_NAME]: userId };
}
