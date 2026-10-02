    const numberFormat = new Intl.NumberFormat('ko-KR');
    const compactFormat = new Intl.NumberFormat('en-US', { notation: 'compact', maximumFractionDigits: 1 });
    const $ = id => document.getElementById(id);
    let selectedRange = 30;
    let latestDailyUsage = [];
    let visibleDailyUsage = [];
    let chartSignature = '';
    let selectedActivityDate = null;

    function getInitialTheme() {
      const savedTheme = document.documentElement.getAttribute('data-theme');
      if (savedTheme === 'light' || savedTheme === 'dark') return savedTheme;
      return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches
        ? 'dark'
        : 'light';
    }

    function applyTheme(theme) {
      const isDark = theme === 'dark';
      document.documentElement.setAttribute('data-theme', isDark ? 'dark' : 'light');
      const label = isDark ? '라이트 모드로 전환' : '다크 모드로 전환';
      $('themeButton').setAttribute('aria-label', label);
      $('themeButton').title = label;
      $('themeIcon').textContent = isDark ? '☀' : '☾';
    }

    $('themeButton').addEventListener('click', async () => {
      const button = $('themeButton');
      const previousTheme = document.documentElement.getAttribute('data-theme') === 'dark' ? 'dark' : 'light';
      const nextTheme = previousTheme === 'dark' ? 'light' : 'dark';
      applyTheme(nextTheme);
      button.disabled = true;
      try {
        const response = await fetch(`/api/theme/${nextTheme}`, { method: 'POST' });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
      } catch (error) {
        applyTheme(previousTheme);
        $('notice').textContent = '테마 설정을 저장하지 못했습니다. 다시 시도하세요.';
        $('notice').classList.add('visible');
      } finally {
        button.disabled = false;
      }
    });

    applyTheme(getInitialTheme());

    function formatNumber(value) {
      return value === null || value === undefined ? '—' : numberFormat.format(value);
    }

    function formatDuration(seconds) {
      if (seconds === null || seconds === undefined) return '—';
      if (seconds < 60) return `${seconds}초`;
      const minutes = Math.floor(seconds / 60);
      const remainder = seconds % 60;
      if (minutes < 60) return remainder ? `${minutes}분 ${remainder}초` : `${minutes}분`;
      const hours = Math.floor(minutes / 60);
      const minuteRemainder = minutes % 60;
      return minuteRemainder ? `${hours}시간 ${minuteRemainder}분` : `${hours}시간`;
    }

    function formatReset(value) {
      if (!value) return '초기화 시각 없음';
      return `초기화 ${new Intl.DateTimeFormat('ko-KR', {
        month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit'
      }).format(new Date(value))}`;
    }

    function getVisibleDailyUsage(items, range) {
      const valid = (Array.isArray(items) ? items : []).filter(item => {
        if (!item || typeof item.date !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(item.date)) return false;
        const date = new Date(`${item.date}T00:00:00Z`);
        return Number.isFinite(date.getTime()) && date.toISOString().slice(0, 10) === item.date &&
          Number.isFinite(item.tokens) && item.tokens >= 0;
      }).slice().sort((left, right) => left.date.localeCompare(right.date));
      if (!valid.length) return [];
      const lastDate = new Date(`${valid[valid.length - 1].date}T00:00:00Z`);
      lastDate.setUTCDate(lastDate.getUTCDate() - range + 1);
      const firstDate = lastDate.toISOString().slice(0, 10);
      return valid.filter(item => item.date >= firstDate);
    }

    function summarizeDailyUsage(items) {
      const weekdayTotals = ['월', '화', '수', '목', '금', '토', '일'].map(label => ({ label, tokens: 0 }));
      let total = 0;
      const cumulative = items.map(item => {
        total += item.tokens;
        const weekday = (new Date(`${item.date}T00:00:00Z`).getUTCDay() + 6) % 7;
        weekdayTotals[weekday].tokens += item.tokens;
        return { date: item.date, tokens: total };
      });
      return { total, cumulative, weekdayTotals };
    }

    function renderQuota(limits) {
      const preferred = [
        limits.find(item => item.durationMinutes === 300),
        limits.find(item => item.durationMinutes === 10080)
      ].filter(Boolean);
      const visible = preferred.length ? preferred : limits.slice(0, 2);
      const container = $('quotaList');
      container.replaceChildren();

      if (!visible.length) {
        const empty = document.createElement('div');
        empty.className = 'empty-chart';
        empty.textContent = '표시할 사용량 한도가 없습니다.';
        container.appendChild(empty);
        return;
      }

      visible.forEach(item => {
        const row = document.createElement('div');
        row.className = 'quota-row';
        const percent = Number.isFinite(item.remainingPercent)
          ? Math.max(0, Math.min(100, item.remainingPercent)) : 0;
        row.innerHTML = `
          <div class="quota-meta">
            <div class="quota-name"></div>
            <div class="quota-number">${percent}<small>% 남음</small></div>
          </div>
          <div class="horizon" role="meter" aria-label="남은 사용량" aria-valuenow="${percent}" aria-valuemin="0" aria-valuemax="100">
            <div class="horizon-fill" style="width:${percent}%"></div>
          </div>
          <div class="quota-scale"><span>${numberFormat.format(100 - percent)}% 사용</span><span>100% 한도</span></div>
          <div class="quota-reset"></div>`;
        row.querySelector('.quota-name').textContent = item.label || '사용량';
        row.querySelector('.quota-reset').textContent = formatReset(item.resetsAt);
        container.appendChild(row);
      });
    }

    function emptyChart(container) {
      container.replaceChildren();
      const empty = document.createElement('div');
      empty.className = 'empty-chart';
      empty.textContent = '계정에서 일별 토큰 통계를 제공하지 않았습니다.';
      container.appendChild(empty);
    }

    function svgElement(name, attributes, text) {
      const element = document.createElementNS('http://www.w3.org/2000/svg', name);
      Object.entries(attributes || {}).forEach(([key, value]) => element.setAttribute(key, value));
      if (text !== undefined) element.textContent = text;
      return element;
    }

    function renderSvgChart(id, items, cumulative) {
      const chart = $(id);
      if (!items.length) { emptyChart(chart); return; }
      chart.replaceChildren();
      const width = Math.max(chart.clientWidth, 160);
      const height = cumulative ? 220 : 260;
      const left = 49, right = 9, top = 28, bottom = 40;
      const plotWidth = width - left - right, plotHeight = height - top - bottom;
      const maximum = Math.max(...items.map(item => item.tokens));
      const power = Math.pow(10, Math.floor(Math.log10(maximum || 1)));
      const ceiling = maximum ? [1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10].find(step => step * power >= maximum) * power : 4;
      const x = index => left + (cumulative
        ? (items.length === 1 ? .5 : index / (items.length - 1))
        : (index + .5) / items.length) * plotWidth;
      const y = value => top + plotHeight - value / ceiling * plotHeight;
      const svg = svgElement('svg', { class: 'chart-plot', viewBox: `0 0 ${width} ${height}`, role: 'group',
        'aria-label': `${cumulative ? '기간 내 누적' : '일별'} 토큰, ${items[0].date}부터 ${items[items.length - 1].date}까지` });
      svg.appendChild(svgElement('text', { class: 'chart-axis', x: 0, y: 12 }, '토큰'));
      for (let tick = 0; tick <= 4; tick++) {
        const value = ceiling * tick / 4;
        svg.appendChild(svgElement('line', { class: 'chart-grid', x1: left, x2: width - right, y1: y(value), y2: y(value) }));
        svg.appendChild(svgElement('text', { class: 'chart-axis', x: left - 8, y: y(value) + 4, 'text-anchor': 'end' }, compactFormat.format(value)));
      }
      const ticks = Math.min(items.length, width < 360 ? 3 : 5);
      for (let tick = 0; tick < ticks; tick++) {
        const index = ticks === 1 ? 0 : Math.round(tick * (items.length - 1) / (ticks - 1));
        svg.appendChild(svgElement('text', { class: 'chart-axis', x: x(index), y: height - 20,
          'text-anchor': ticks === 1 ? 'middle' : tick === 0 ? 'start' : tick === ticks - 1 ? 'end' : 'middle' },
          items[index].date.slice(5).replace('-', '/')));
      }
      svg.appendChild(svgElement('text', { class: 'chart-axis', x: width - right, y: height - 2, 'text-anchor': 'end' }, '날짜'));
      if (cumulative) {
        const d = items.map((item, index) => `${index ? 'L' : 'M'}${x(index)},${y(item.tokens)}`).join(' ');
        svg.appendChild(svgElement('path', { class: 'chart-line', d }));
      }
      const tooltip = document.createElement('div');
      tooltip.className = 'bar-tooltip';
      tooltip.id = `${id}-tooltip`;
      tooltip.setAttribute('role', 'tooltip');
      const tooltipDate = document.createElement('span');
      tooltipDate.className = 'bar-tooltip-date';
      const tooltipValue = document.createElement('strong');
      tooltip.append(tooltipDate, tooltipValue);

      items.forEach((item, index) => {
        const group = svgElement('g', { class: 'bar-wrap', tabindex: 0, role: 'img',
          'aria-label': `${item.date} ${cumulative ? '누적 ' : ''}${numberFormat.format(item.tokens)} 토큰`,
          'aria-describedby': tooltip.id });
        let mark;
        if (cumulative) {
          mark = svgElement('circle', { class: 'chart-point', cx: x(index), cy: y(item.tokens), r: 2.5 });
          const hitRadius = items.length === 1 ? 12 : Math.min(12, plotWidth / (items.length - 1) / 2);
          group.append(mark, svgElement('circle', { class: 'chart-hit', cx: x(index), cy: y(item.tokens), r: hitRadius }));
        } else {
          const space = plotWidth / items.length;
          const barWidth = Math.max(2, space * .62);
          mark = svgElement('rect', { class: 'chart-bar', x: x(index) - barWidth / 2, y: y(item.tokens),
            width: barWidth, height: top + plotHeight - y(item.tokens), rx: 1.5 });
          group.append(mark, svgElement('rect', { class: 'chart-hit', x: x(index) - space / 2,
            y: top, width: space, height: plotHeight }));
        }
        const showTooltip = () => {
          tooltipDate.textContent = item.date;
          tooltipValue.textContent = `${cumulative ? '누적 ' : ''}${numberFormat.format(item.tokens)} 토큰`;
          tooltip.classList.add('visible');
          const box = mark.getBoundingClientRect(), bounds = chart.getBoundingClientRect();
          tooltip.style.left = `${Math.max(0, Math.min(width - tooltip.offsetWidth, box.left - bounds.left + box.width / 2 - tooltip.offsetWidth / 2))}px`;
          tooltip.style.top = `${Math.max(0, box.top - bounds.top - tooltip.offsetHeight - 8)}px`;
        };
        group.addEventListener('pointerenter', showTooltip);
        group.addEventListener('focus', showTooltip);
        group.addEventListener('click', showTooltip);
        group.addEventListener('pointerleave', () => { if (document.activeElement !== group) tooltip.classList.remove('visible'); });
        group.addEventListener('blur', () => tooltip.classList.remove('visible'));
        svg.appendChild(group);
      });
      chart.append(svg, tooltip);
    }

    function renderWeekdays(items) {
      const chart = $('weekdayChart');
      chart.replaceChildren();
      const maximum = Math.max(...items.map(item => item.tokens), 1);
      items.forEach(item => {
        const row = document.createElement('div');
        row.className = 'weekday-row';
        row.setAttribute('aria-label', `${item.label}요일 ${numberFormat.format(item.tokens)} 토큰`);
        row.innerHTML = `<span>${item.label}</span><div class="weekday-track"><div class="weekday-fill" style="width:${item.tokens / maximum * 100}%"></div></div><span class="weekday-number">${compactFormat.format(item.tokens)}</span>`;
        row.title = `${item.label}요일 ${numberFormat.format(item.tokens)} 토큰`;
        chart.appendChild(row);
      });
    }

    function renderActivity(items) {
      const grid = $('activityGrid');
      grid.replaceChildren();
      const maximum = Math.max(...items.map(item => item.tokens), 1);
      if (!items.some(item => item.date === selectedActivityDate)) selectedActivityDate = null;
      items.forEach(item => {
        const cell = document.createElement('button');
        cell.type = 'button';
        cell.className = 'heatmap-cell';
        cell.textContent = Number(item.date.slice(8));
        cell.dataset.level = item.tokens ? Math.ceil(item.tokens / maximum * 4) : 0;
        cell.title = `${item.date} · ${numberFormat.format(item.tokens)} 토큰`;
        cell.setAttribute('aria-label', cell.title);
        cell.setAttribute('aria-pressed', String(item.date === selectedActivityDate));
        cell.addEventListener('click', () => {
          selectedActivityDate = item.date;
          grid.querySelectorAll('button').forEach(button => button.setAttribute('aria-pressed', String(button === cell)));
          $('activityDetail').textContent = cell.title;
        });
        grid.appendChild(cell);
      });
      const selected = items.find(item => item.date === selectedActivityDate);
      $('activityDetail').textContent = selected
        ? `${selected.date} · ${numberFormat.format(selected.tokens)} 토큰`
        : `기록된 ${items.length}일 · 날짜를 선택하면 해당 기록을 확인할 수 있습니다.`;
    }

    function renderChart(items) {
      latestDailyUsage = items;
      visibleDailyUsage = getVisibleDailyUsage(items, selectedRange);
      const signature = JSON.stringify([selectedRange, visibleDailyUsage]);
      if (signature === chartSignature) return;
      chartSignature = signature;
      const summary = summarizeDailyUsage(visibleDailyUsage);
      $('periodLabel').textContent = `최근 ${selectedRange}일 토큰`;
      $('dailyDescription').textContent = `최근 ${selectedRange}일 · 단위: 토큰`;
      $('periodTokens').textContent = visibleDailyUsage.length ? compactFormat.format(summary.total) : '—';
      $('chartTotal').textContent = visibleDailyUsage.length ? `표시 기간 합계 ${numberFormat.format(summary.total)} 토큰` : '표시 기간 합계 —';
      $('periodDates').textContent = visibleDailyUsage.length
        ? `${visibleDailyUsage[0].date} — ${visibleDailyUsage[visibleDailyUsage.length - 1].date}` : '제공된 일별 기록이 없습니다.';
      renderSvgChart('dailyChart', visibleDailyUsage, false);
      renderSvgChart('cumulativeChart', summary.cumulative, true);
      if (visibleDailyUsage.length) {
        renderWeekdays(summary.weekdayTotals);
        renderActivity(visibleDailyUsage);
      } else {
        emptyChart($('weekdayChart'));
        emptyChart($('activityGrid'));
        $('activityDetail').textContent = '제공된 일별 기록이 없습니다.';
      }
    }

    function render(data) {
      const state = data.status || 'loading';
      $('liveState').className = `live-state ${state}`;
      $('liveText').textContent = state === 'ready' ? '정상 연결' :
        state === 'loading' ? '연결 중' : state === 'warning' ? '일부 정보 없음' : '이전 정보 표시';

      const notice = $('notice');
      notice.textContent = data.message || '';
      notice.classList.toggle('visible', Boolean(data.message));

      const account = data.account || {};
      $('accountEmail').textContent = account.email || (account.type === 'apiKey' ? 'API 키 계정' : '계정 정보 없음');
      $('planType').textContent = account.planType || account.type || '—';

      const usage = data.usage || {};
      $('lifetimeTokens').replaceChildren(document.createTextNode(
        usage.lifetimeTokens === null || usage.lifetimeTokens === undefined ? '—' : compactFormat.format(usage.lifetimeTokens)
      ));
      $('lifetimeExact').textContent = usage.lifetimeTokens === null || usage.lifetimeTokens === undefined
        ? '전체 활동 누계' : `${formatNumber(usage.lifetimeTokens)} 토큰`;
      $('peakDaily').textContent = usage.peakDailyTokens === null || usage.peakDailyTokens === undefined
        ? '—' : compactFormat.format(usage.peakDailyTokens);
      $('peakDaily').title = usage.peakDailyTokens === null || usage.peakDailyTokens === undefined
        ? '' : `${formatNumber(usage.peakDailyTokens)} 토큰`;
      $('currentStreak').textContent = usage.currentStreakDays === null || usage.currentStreakDays === undefined
        ? '—' : `${formatNumber(usage.currentStreakDays)}일`;
      $('streakRecord').textContent = usage.longestStreakDays === null || usage.longestStreakDays === undefined
        ? '최장 기록 없음' : `최장 기록 ${formatNumber(usage.longestStreakDays)}일`;
      $('longestStreak').textContent = usage.longestStreakDays === null || usage.longestStreakDays === undefined
        ? '—' : `${formatNumber(usage.longestStreakDays)}일`;
      $('longestTurn').textContent = formatDuration(usage.longestRunningTurnSec);

      renderQuota(data.rateLimits || []);
      renderChart(usage.dailyUsage || []);
      $('updatedAt').textContent = data.updatedAt
        ? `마지막 갱신 ${new Intl.DateTimeFormat('ko-KR', { dateStyle: 'medium', timeStyle: 'medium' }).format(new Date(data.updatedAt))}`
        : '아직 갱신되지 않았습니다.';
    }

    async function loadSnapshot(silent = false) {
      try {
        const response = await fetch('/api/snapshot', { cache: 'no-store' });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        render(await response.json());
      } catch (error) {
        if (!silent) {
          $('notice').textContent = '대시보드 데이터를 읽지 못했습니다. 앱이 실행 중인지 확인하세요.';
          $('notice').classList.add('visible');
        }
        $('liveState').className = 'live-state stale';
        $('liveText').textContent = '연결 끊김';
      }
    }

    $('refreshButton').addEventListener('click', async () => {
      const button = $('refreshButton');
      button.disabled = true;
      button.textContent = '갱신 중...';
      try {
        await fetch('/api/refresh', { method: 'POST' });
        await new Promise(resolve => setTimeout(resolve, 900));
        await loadSnapshot();
      } finally {
        button.disabled = false;
        button.textContent = '지금 갱신';
      }
    });

    document.querySelectorAll('.period-selector button').forEach(button => {
      button.addEventListener('click', () => {
        selectedRange = Number(button.dataset.period);
        document.querySelectorAll('.period-selector button').forEach(item =>
          item.setAttribute('aria-pressed', String(item === button)));
        renderChart(latestDailyUsage);
      });
    });

    const chartResize = new ResizeObserver(() => {
      if (!visibleDailyUsage.length) return;
      renderSvgChart('dailyChart', visibleDailyUsage, false);
      renderSvgChart('cumulativeChart', summarizeDailyUsage(visibleDailyUsage).cumulative, true);
    });
    chartResize.observe($('dailyChart'));
    chartResize.observe($('cumulativeChart'));

    function updateNavigation() {
      document.querySelectorAll('.navigation a').forEach(link => {
        if (link.getAttribute('href') === (window.location.hash || '#overview')) link.setAttribute('aria-current', 'location');
        else link.removeAttribute('aria-current');
      });
    }
    window.addEventListener('hashchange', updateNavigation);
    updateNavigation();

    loadSnapshot();
    setInterval(() => loadSnapshot(true), 10000);
