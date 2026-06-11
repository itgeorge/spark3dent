(function () {
  const DEFAULT_WEEKDAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
  const DEFAULT_TITLE_FORMATTER = new Intl.DateTimeFormat('en-US', { month: 'long', year: 'numeric' });

  function addDays(date, days) {
    const next = new Date(date);
    next.setDate(next.getDate() + days);
    return next;
  }

  function toIsoDate(date) {
    return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
  }

  function startOfMonth(date) {
    return new Date(date.getFullYear(), date.getMonth(), 1);
  }

  function bounds(monthDate) {
    const first = startOfMonth(monthDate);
    const last = new Date(first.getFullYear(), first.getMonth() + 1, 0);
    const start = addDays(first, -((first.getDay() + 6) % 7));
    const end = addDays(last, 6 - ((last.getDay() + 6) % 7));
    return { first, start, end };
  }

  function isSameMonth(date, monthDate) {
    return date.getFullYear() === monthDate.getFullYear() && date.getMonth() === monthDate.getMonth();
  }

  function isSameDay(a, b) {
    return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
  }

  function normalizeNonWorkingDays(value) {
    if (!value) return null;
    if (value instanceof Set) return value;
    if (Array.isArray(value)) return new Set(value);
    return null;
  }

  class MonthCalendar {
    constructor(root, options = {}) {
      if (!root) throw new Error('MonthCalendar root is required.');
      this.root = root;
      this.options = { ...options };
      this.month = startOfMonth(options.month || new Date());
      this.root.classList.add('month-calendar');
      if (options.className) this.root.classList.add(options.className);
      this.root.innerHTML = `
        <div class="month-calendar-head">
          <button class="btn month-calendar-nav" type="button" data-month-calendar-prev aria-label="Previous month"><svg class="month-calendar-nav-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M15 18l-6-6 6-6" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"></path></svg></button>
          <div class="month-calendar-title" data-month-calendar-title>—</div>
          <button class="btn month-calendar-nav" type="button" data-month-calendar-next aria-label="Next month"><svg class="month-calendar-nav-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M9 6l6 6-6 6" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"></path></svg></button>
        </div>
        <div class="month-calendar-grid" data-month-calendar-grid></div>`;
      this.titleEl = this.root.querySelector('[data-month-calendar-title]');
      this.gridEl = this.root.querySelector('[data-month-calendar-grid]');
      this.root.querySelector('[data-month-calendar-prev]').addEventListener('click', () => this.changeMonth(-1));
      this.root.querySelector('[data-month-calendar-next]').addEventListener('click', () => this.changeMonth(1));
      this.render();
    }

    setOptions(options = {}) {
      if (options.className && options.className !== this.options.className) {
        if (this.options.className) this.root.classList.remove(this.options.className);
        this.root.classList.add(options.className);
      }
      this.options = { ...this.options, ...options };
      if (options.month) this.month = startOfMonth(options.month);
      this.render();
    }

    setMonth(month) {
      this.month = startOfMonth(month);
      this.render();
    }

    changeMonth(delta) {
      this.month = new Date(this.month.getFullYear(), this.month.getMonth() + delta, 1);
      this.render();
      if (typeof this.options.onMonthChange === 'function') this.options.onMonthChange(this.month);
    }

    render() {
      const weekdays = this.options.weekdays || DEFAULT_WEEKDAYS;
      const titleFormatter = this.options.titleFormatter || DEFAULT_TITLE_FORMATTER;
      const nonWorkingDays = normalizeNonWorkingDays(this.options.nonWorkingDays);
      this.titleEl.textContent = titleFormatter.format(this.month);
      const today = new Date();
      const b = bounds(this.month);
      const frag = document.createDocumentFragment();

      weekdays.forEach(day => {
        const header = document.createElement('div');
        header.className = 'month-calendar-weekday';
        header.textContent = day;
        frag.appendChild(header);
      });

      for (let day = new Date(b.start); day <= b.end; day = addDays(day, 1)) {
        const iso = toIsoDate(day);
        const outsideMonth = !isSameMonth(day, this.month);
        const isNonWorkingDay = !!nonWorkingDays?.has(iso);
        const cell = document.createElement('div');
        cell.className = ['month-calendar-cell', outsideMonth ? 'outside-month' : '', isNonWorkingDay ? 'non-working-day' : '', isSameDay(day, today) ? 'today' : ''].filter(Boolean).join(' ');
        cell.dataset.date = iso;
        const dayHead = document.createElement('div');
        dayHead.className = 'month-calendar-day-head';
        const num = document.createElement('span');
        num.className = 'month-calendar-day-number';
        num.textContent = String(day.getDate());
        const wd = document.createElement('span');
        wd.className = 'month-calendar-day-weekday';
        wd.textContent = weekdays[(day.getDay() + 6) % 7].slice(0, 3);
        dayHead.append(num, wd);
        const content = document.createElement('div');
        content.className = 'month-calendar-cell-content';
        cell.append(dayHead, content);

        if (typeof this.options.renderCell === 'function') {
          this.options.renderCell({
            cell,
            content,
            date: new Date(day),
            iso,
            outsideMonth,
            isNonWorkingDay,
            isToday: isSameDay(day, today),
            weekdayLabel: weekdays[(day.getDay() + 6) % 7]
          });
        }
        frag.appendChild(cell);
      }

      this.gridEl.replaceChildren(frag);
    }
  }

  window.MonthCalendar = {
    create: (root, options) => new MonthCalendar(root, options),
    bounds,
    addDays,
    toIsoDate,
    isSameMonth
  };
})();
