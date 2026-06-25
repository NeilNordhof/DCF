import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi } from 'vitest';
import { TimePicker } from './TimePicker';

// Helpers — DOM order of buttons: [hourUp, hourDown, minuteUp, minuteDown]
function getButtons() {
  const [hourUp, hourDown, minuteUp, minuteDown] = screen.getAllByRole('button');

  return { hourUp, hourDown, minuteUp, minuteDown };
}

function getInputs() {
  const [hour, minute] = screen.getAllByRole('textbox');

  return { hour, minute };
}

describe('TimePicker', () => {
  describe('hour arrows', () => {
    it('increments hour on up click', async () => {
      const onChange = vi.fn();
      render(<TimePicker value="09:30" onChange={onChange} />);

      await userEvent.click(getButtons().hourUp);

      expect(onChange).toHaveBeenCalledWith('10:30');
    });

    it('decrements hour on down click', async () => {
      const onChange = vi.fn();
      render(<TimePicker value="09:30" onChange={onChange} />);

      await userEvent.click(getButtons().hourDown);

      expect(onChange).toHaveBeenCalledWith('08:30');
    });

    it('wraps 12 up to 1 (same AM/PM), so 12:00 PM -> 13:00', async () => {
      const onChange = vi.fn();
      // 12:00 24h = 12:00 PM; click up → 1 PM = 13:00
      render(<TimePicker value="12:00" onChange={onChange} />);

      await userEvent.click(getButtons().hourUp);

      expect(onChange).toHaveBeenCalledWith('13:00');
    });

    it('wraps 1 AM down to 12 AM (midnight), so 01:00 -> 00:00', async () => {
      const onChange = vi.fn();
      // 01:00 24h = 1:00 AM; click down → 12 AM = 00:00
      render(<TimePicker value="01:00" onChange={onChange} />);

      await userEvent.click(getButtons().hourDown);

      expect(onChange).toHaveBeenCalledWith('00:00');
    });
  });

  describe('minute arrows', () => {
    it('increments minute by 5', async () => {
      const onChange = vi.fn();
      render(<TimePicker value="09:30" onChange={onChange} />);

      await userEvent.click(getButtons().minuteUp);

      expect(onChange).toHaveBeenCalledWith('09:35');
    });

    it('decrements minute by 5', async () => {
      const onChange = vi.fn();
      render(<TimePicker value="09:30" onChange={onChange} />);

      await userEvent.click(getButtons().minuteDown);

      expect(onChange).toHaveBeenCalledWith('09:25');
    });

    it('carries into next hour when minute wraps 55 -> 00', async () => {
      const onChange = vi.fn();
      render(<TimePicker value="09:55" onChange={onChange} />);

      await userEvent.click(getButtons().minuteUp);

      expect(onChange).toHaveBeenCalledWith('10:00');
    });

    it('borrows from hour when minute wraps 00 -> 55', async () => {
      const onChange = vi.fn();
      render(<TimePicker value="09:00" onChange={onChange} />);

      await userEvent.click(getButtons().minuteDown);

      expect(onChange).toHaveBeenCalledWith('08:55');
    });
  });

  describe('AM/PM select', () => {
    it('switching AM to PM adds 12 hours', async () => {
      const onChange = vi.fn();
      render(<TimePicker value="09:30" onChange={onChange} />); // 9:30 AM

      await userEvent.selectOptions(screen.getByRole('combobox'), 'PM');

      expect(onChange).toHaveBeenCalledWith('21:30');
    });

    it('switching PM to AM subtracts 12 hours', async () => {
      const onChange = vi.fn();
      render(<TimePicker value="21:30" onChange={onChange} />); // 9:30 PM

      await userEvent.selectOptions(screen.getByRole('combobox'), 'AM');

      expect(onChange).toHaveBeenCalledWith('09:30');
    });

    it('12 PM stays 12 when switching to AM (becomes midnight 00:00)', async () => {
      const onChange = vi.fn();
      render(<TimePicker value="12:00" onChange={onChange} />); // 12:00 PM = noon

      await userEvent.selectOptions(screen.getByRole('combobox'), 'AM');

      expect(onChange).toHaveBeenCalledWith('00:00');
    });
  });

  describe('empty state', () => {
    it('displays -- in both inputs when no value', () => {
      render(<TimePicker value="" onChange={vi.fn()} />);
      const { hour, minute } = getInputs();

      expect(hour).toHaveValue('--');
      expect(minute).toHaveValue('--');
    });

    it('initialises to 12:00 PM and emits "12:00" on first arrow click', async () => {
      const onChange = vi.fn();
      render(<TimePicker value="" onChange={onChange} />);

      await userEvent.click(getButtons().hourUp);

      expect(onChange).toHaveBeenCalledWith('12:00');
    });
  });

  describe('value prop sync', () => {
    it('updates displayed hour and minute when value prop changes', () => {
      const { rerender } = render(<TimePicker value="09:30" onChange={vi.fn()} />);

      rerender(<TimePicker value="14:00" onChange={vi.fn()} />); // 2:00 PM

      const { hour, minute } = getInputs();

      expect(hour).toHaveValue('02');
      expect(minute).toHaveValue('00');
    });
  });

  describe('required=false', () => {
    it('emits "" when hour field is cleared and blurred', async () => {
      const onChange = vi.fn();
      render(<TimePicker value="09:30" onChange={onChange} required={false} />);

      await userEvent.click(getInputs().hour);
      await userEvent.clear(getInputs().hour);
      await userEvent.tab();

      expect(onChange).toHaveBeenCalledWith('');
    });
  });
});
