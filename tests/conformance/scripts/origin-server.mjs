import { createServer } from 'node:http'
import { readFile } from 'node:fs/promises'
import path from 'node:path'

// Two explicit loopback origins: only the first is granted by the fixture ACL.
// Never listen on an external interface or serve files outside the test bundle.
export async function startOriginServers(dist) {
  const reports = new Map()
  const servers = []
  const close = () => Promise.all(servers.map(server => new Promise(resolve => {
    server.close(resolve)
    server.closeAllConnections()
  })))
  try {
    for (const port of [18191, 18192]) {
      const server = createServer(async (req, res) => {
        res.setHeader('Access-Control-Allow-Origin', '*')
        res.setHeader('Access-Control-Allow-Headers', 'content-type')
        res.setHeader('Cache-Control', 'no-store')
        const url = new URL(req.url, 'http://127.0.0.1')
        try {
          if (req.method === 'OPTIONS') { res.writeHead(204).end(); return }
          if (url.pathname === '/report') {
            const id = url.searchParams.get('id')
            if (req.method === 'POST') {
              let body = ''
              for await (const chunk of req) {
                body += chunk
                if (body.length > 65536) throw Error('Probe report too large')
              }
              reports.set(id, JSON.parse(body))
              res.writeHead(204).end()
            } else {
              res.setHeader('Content-Type', 'application/json')
              res.end(JSON.stringify(reports.get(id) ?? null))
            }
            return
          }
          const allowed = ['/frame-probe.html', '/vendor/@tauri-apps/api/core.js', '/vendor/@tauri-apps/api/external/tslib/tslib.es6.js']
          if (!allowed.includes(url.pathname)) { res.writeHead(404).end(); return }
          res.setHeader('Content-Type', url.pathname.endsWith('.js') ? 'text/javascript' : 'text/html')
          res.end(await readFile(path.join(dist, url.pathname.slice(1))))
        } catch (error) { res.writeHead(500).end(String(error)) }
      })
      servers.push(server)
      await new Promise((resolve, reject) => {
        server.once('error', reject)
        server.listen(port, '127.0.0.1', resolve)
      })
    }
    return { close }
  } catch (error) { await close(); throw error }
}
