import { ref } from 'vue'

const KEY = 'supplyguard.accessToken'

// sessionStorage: the token survives a reload but not a closed tab. Access tokens expire after 15 minutes.
const token = ref<string | null>(sessionStorage.getItem(KEY))

export function useSession() {
  return {
    token,
    signIn(accessToken: string) {
      token.value = accessToken
      sessionStorage.setItem(KEY, accessToken)
    },
    signOut() {
      token.value = null
      sessionStorage.removeItem(KEY)
    }
  }
}
