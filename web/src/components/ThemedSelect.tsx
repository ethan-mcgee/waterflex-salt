import * as Select from '@radix-ui/react-select';
import {
  Check,
  ChevronDown,
  ChevronUp,
} from 'lucide-react';

export interface ThemedSelectOption {
  value: string;
  label: string;
}

interface ThemedSelectProps {
  value: string;
  options: ThemedSelectOption[];
  onValueChange: (value: string) => void;
  ariaLabel: string;
  disabled?: boolean;
  placeholder?: string;
}

const EMPTY_VALUE = '__waterflex_empty__';

/** Theme-styled wrapper around Radix `Select` for the console's dropdowns. */
export default function ThemedSelect({
  value,
  options,
  onValueChange,
  ariaLabel,
  disabled = false,
  placeholder,
}: ThemedSelectProps) {
  const selectedOption = options.find((option) => option.value === value);

  return (
    <Select.Root
      value={toInternalValue(value)}
      disabled={disabled}
      onValueChange={(nextValue) => onValueChange(fromInternalValue(nextValue))}
    >
      <Select.Trigger className="themed-select-trigger" aria-label={ariaLabel}>
        <Select.Value className="themed-select-value">
          {selectedOption?.label ?? placeholder ?? value}
        </Select.Value>
        <Select.Icon className="themed-select-chevron">
          <ChevronDown size={14} />
        </Select.Icon>
      </Select.Trigger>
      <Select.Portal>
        <Select.Content
          className="themed-select-content"
          position="popper"
          sideOffset={5}
          collisionPadding={10}
        >
          <Select.ScrollUpButton className="themed-select-scroll-button">
            <ChevronUp size={14} />
          </Select.ScrollUpButton>
          <Select.Viewport className="themed-select-viewport">
            {options.map((option) => (
              <Select.Item
                className="themed-select-item"
                key={option.value || EMPTY_VALUE}
                value={toInternalValue(option.value)}
              >
                <Select.ItemText>{option.label}</Select.ItemText>
                <Select.ItemIndicator className="themed-select-indicator">
                  <Check size={14} />
                </Select.ItemIndicator>
              </Select.Item>
            ))}
          </Select.Viewport>
          <Select.ScrollDownButton className="themed-select-scroll-button">
            <ChevronDown size={14} />
          </Select.ScrollDownButton>
        </Select.Content>
      </Select.Portal>
    </Select.Root>
  );
}

const toInternalValue = (value: string): string => value || EMPTY_VALUE;

const fromInternalValue = (value: string): string => value === EMPTY_VALUE ? '' : value;