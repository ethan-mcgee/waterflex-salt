import { UserRound } from 'lucide-react';
import {
  createContext,
  useContext,
  useEffect,
  useState,
} from 'react';
import type { ReactNode } from 'react';
import ThemedSelect from '../components/ThemedSelect';

export type StaffRole = 'dealerTechnician' | 'waterFlexEmployee';

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

const HEADER_NAME = 'X-WaterFlex-Development-User';
const STORAGE_KEY = 'waterflex-development-user';
const DEFAULT_USER_ID = 'wf-ops-alex';
const DevelopmentIdentityContext = createContext<DevelopmentIdentityValue | null>(null);

export function DevelopmentIdentityProvider({ children }: { children: ReactNode }) {
  const [users, setUsers] = useState<DevelopmentUser[]>([]);
  const [selectedUserId, setSelectedUserId] = useState(() =>
    window.localStorage.getItem(STORAGE_KEY) || DEFAULT_USER_ID);

  useEffect(() => {
    const controller = new AbortController();
    const identityUrl = import.meta.env.DEV
      ? '/api/v1/development/users'
      : '/api/v1/staff/me';
    fetch(identityUrl, { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) return [];
        const payload = await response.json() as DevelopmentUser[] | DevelopmentUser;
        return Array.isArray(payload) ? payload : [payload];
      })
      .then((availableUsers) => {
        setUsers(availableUsers);
        if (availableUsers.length > 0
          && !availableUsers.some((user) => user.userId === selectedUserId)) {
          const fallback = availableUsers.find((user) => user.role === 'waterFlexEmployee')
            ?? availableUsers[0];
          selectUser(fallback.userId);
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) setUsers([]);
      });
    return () => controller.abort();
  }, [selectedUserId]);

  function selectUser(userId: string) {
    window.localStorage.setItem(STORAGE_KEY, userId);
    setSelectedUserId(userId);
  }

  const currentUser = users.find((user) => user.userId === selectedUserId) ?? null;
  return (
    <DevelopmentIdentityContext.Provider value={{ users, selectedUserId, currentUser, selectUser }}>
      {children}
    </DevelopmentIdentityContext.Provider>
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
