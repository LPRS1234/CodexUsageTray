    const numberFormat = new Intl.NumberFormat('ko-KR');
    const compactFormat = new Intl.NumberFormat('ko-KR', { notation: 'compact', maximumFractionDigits: 1 });
    const $ = id => document.getElementById(id);

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
        const percent = Math.max(0, Math.min(100, item.remainingPercent));
        row.innerHTML = `
          <div class="quota-meta">
            <div><div class="quota-name"></div><div class="quota-reset"></div></div>
            <div class="quota-number">${percent}<small>% 남음</small></div>
          </div>
          <div class="horizon" role="img" aria-label="${percent}% 남음">
            <div class="horizon-fill" style="width:${percent}%"></div>
            <div class="horizon-marker" style="left:${percent}%"></div>
          </div>`;
        row.querySelector('.quota-name').textContent = item.label || '사용량';
        row.querySelector('.quota-reset').textContent = formatReset(item.resetsAt);
        container.appendChild(row);
      });
    }

    function renderChart(items) {
      const chart = $('dailyChart');
      chart.replaceChildren();
      const visible = (items || []).slice(-30);
      if (!visible.length) {
        const empty = document.createElement('div');
        empty.className = 'empty-chart';
        empty.textContent = '계정에서 일별 토큰 통계를 제공하지 않았습니다.';
        chart.appendChild(empty);
        $('chartTotal').textContent = '—';
        return;
      }

      const max = Math.max(...visible.map(item => item.tokens), 1);
      const total = visible.reduce((sum, item) => sum + item.tokens, 0);
      $('chartTotal').textContent = compactFormat.format(total) + ' 토큰';
      visible.forEach((item, index) => {
        const wrap = document.createElement('div');
        wrap.className = 'bar-wrap';
        wrap.title = `${item.date} · ${numberFormat.format(item.tokens)} 토큰`;
        wrap.setAttribute('aria-label', wrap.title);
        const bar = document.createElement('div');
        bar.className = 'bar';
        bar.style.height = `${Math.max(2, item.tokens / max * 100)}%`;
        bar.style.animationDelay = `${index * 16}ms`;
        wrap.appendChild(bar);
        if (index === 0 || index === visible.length - 1 || index % 5 === 0) {
          const label = document.createElement('span');
          label.className = 'bar-label';
          const date = new Date(`${item.date}T00:00:00`);
          label.textContent = `${date.getMonth() + 1}/${date.getDate()}`;
          wrap.appendChild(label);
        }
        chart.appendChild(wrap);
      });
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
        usage.lifetimeTokens === null || usage.lifetimeTokens === undefined ? '—' : formatNumber(usage.lifetimeTokens)
      ));
      if (usage.lifetimeTokens !== null && usage.lifetimeTokens !== undefined) {
        const unit = document.createElement('span');
        unit.className = 'token-unit';
        unit.textContent = '토큰';
        $('lifetimeTokens').appendChild(unit);
      }
      $('peakDaily').textContent = usage.peakDailyTokens === null || usage.peakDailyTokens === undefined
        ? '—' : `${compactFormat.format(usage.peakDailyTokens)} 토큰`;
      $('currentStreak').textContent = usage.currentStreakDays === null || usage.currentStreakDays === undefined
        ? '—' : `${formatNumber(usage.currentStreakDays)}일`;
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

    loadSnapshot();
    setInterval(() => loadSnapshot(true), 10000);
